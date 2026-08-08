using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MemoryTimeline.SyncContracts;
using Xunit;

namespace MemoryTimeline.SyncApi.Tests;

/// <summary>
/// The timeline projection handoff against the real host: Windows publishes
/// <c>event</c>, <c>era</c>, <c>person</c> and <c>pending_event</c> projections
/// onto the change feed so a companion can draw a timeline it does not own, and
/// a companion pushes back exactly one thing — a <c>pending_event_decision</c>,
/// the approve/reject verdict on an extracted event.
///
/// The direction of that traffic is the property worth breaking, so most of
/// what follows tries to: a companion forging an <c>event</c> into someone's
/// timeline, a device signing a decision with another device's ID, a verdict
/// outside the vocabulary, and a delete used to retract one. Alongside those,
/// the ordinary requirements — each type round trips push to pull, a delete
/// tombstones under the same entity ID that created the row, and echo
/// suppression still hands a device its peers' changes and never its own.
///
/// Tests share one host per class, so each takes a per-device baseline cursor
/// before acting.
/// </summary>
public class TimelineProjectionFlowTests : IClassFixture<SyncApiFixture>
{
    /// <summary>Fixed publisher timestamp, so payload round trips can be asserted exactly.</summary>
    private static readonly DateTime UpdatedAt = new(2026, 8, 7, 16, 15, 0, DateTimeKind.Utc);

    private static readonly DateTime EventStart = new(2024, 6, 2, 0, 0, 0, DateTimeKind.Utc);

    private readonly SyncApiFixture _fixture;

    public TimelineProjectionFlowTests(SyncApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Projections_PushedFromWindows_RoundTripToTheCompanion()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (windows, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        var eventId = NewId();
        var eraId = NewId();
        var personId = NewId();
        var pendingEventId = NewId();

        var push = await SyncApiFixture.PushAsync(
            windowsClient,
            EventEntry(1, eventId, payload =>
            {
                payload.EraId = eraId;
                payload.PersonIds = [personId];
                payload.Tags = ["roadtrip", "utah"];
                payload.Locations = ["Green River, Utah"];
                payload.Latitude = 38.9958;
                payload.Longitude = -110.1595;
                payload.MediaCount = 3;
            }),
            EraEntry(2, eraId),
            PersonEntry(3, personId),
            PendingEventEntry(4, pendingEventId));
        push.Results.Should().HaveCount(4);
        push.Results.Should().OnlyContain(r => r.Accepted && !r.Duplicate && r.ServerChangeId != null);

        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);

        // An event carries its tags, people and locations inline: the companion
        // draws a bubble from one payload rather than reassembling junction rows.
        var eventChange = Change(pull, SyncChangeEntityType.Event, eventId);
        eventChange.Operation.Should().Be(SyncOperation.Upsert);
        eventChange.SourceDeviceId.Should().Be(windows.DeviceId);
        eventChange.ChangeId.Should().Be(push.Results[0].ServerChangeId!.Value);
        var projected = Payload<EventProjectionPayload>(eventChange);
        projected.EventId.Should().Be(eventId);
        projected.Title.Should().Be("Camp at Green River");
        projected.StartDate.Should().Be(EventStart);
        projected.DatePrecision.Should().Be("season");
        projected.DisplayDate.Should().Be("Summer 2024", "the publisher formats the date so every consumer agrees");
        projected.EraId.Should().Be(eraId);
        projected.PersonIds.Should().Equal(personId);
        projected.Tags.Should().Equal("roadtrip", "utah");
        projected.Locations.Should().Equal("Green River, Utah");
        projected.Latitude.Should().Be(38.9958);
        projected.MediaCount.Should().Be(3);
        projected.UpdatedAtUtc.Should().Be(UpdatedAt);

        var eraChange = Change(pull, SyncChangeEntityType.Era, eraId);
        var era = Payload<EraProjectionPayload>(eraChange);
        era.EraId.Should().Be(eraId);
        era.Name.Should().Be("Denver years");
        era.ColorCode.Should().Be("#3B7DD8");
        era.EndDate.Should().BeNull("an ongoing era has no end");

        var personChange = Change(pull, SyncChangeEntityType.Person, personId);
        var person = Payload<PersonProjectionPayload>(personChange);
        person.PersonId.Should().Be(personId);
        person.Name.Should().Be("Dana Reyes");
        person.Relationship.Should().Be("sister");
        person.EventCount.Should().Be(12);
        person.MergedIntoId.Should().BeNull();

        var pendingChange = Change(pull, SyncChangeEntityType.PendingEvent, pendingEventId);
        var pending = Payload<PendingEventProjectionPayload>(pendingChange);
        pending.PendingEventId.Should().Be(pendingEventId);
        pending.ConfidenceScore.Should().Be(0.82);
        pending.PeopleNames.Should().Equal("Dana");
        pending.TranscriptPreview.Should().Be("We camped by the river outside Green River.");

        // Every projection is keyed on the projected row's own ID, which is what
        // lets a consumer apply latest-wins and later tombstone the same row.
        new[] { eventChange, eraChange, personChange, pendingChange }
            .Select(c => c.EntityId)
            .Should().Equal(eventId, eraId, personId, pendingEventId);
        new[] { eventChange, eraChange, personChange, pendingChange }
            .Select(c => c.ChangeId)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Decision_PushedFromACompanion_ReachesWindowsWithItsCorrections()
    {
        var (phone, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var phoneBaseline = await SyncApiFixture.GetCursorAsync(phoneClient);
        var windowsBaseline = await SyncApiFixture.GetCursorAsync(windowsClient);
        var pendingEventId = NewId();

        // The one write a companion may author, and it travels the ordinary push
        // path — there is no second transport for review.
        var push = await SyncApiFixture.PushAsync(
            phoneClient,
            DecisionEntry(1, pendingEventId, phone.DeviceId, payload => payload.Corrections = new PendingEventCorrections
            {
                Title = "Camping outside Green River",
                StartDate = EventStart,
                DatePrecision = "day",
            }));
        var result = push.Results.Should().ContainSingle().Which;
        result.Accepted.Should().BeTrue();
        result.Duplicate.Should().BeFalse();

        var windowsPull = await SyncApiFixture.PullAsync(windowsClient, windowsBaseline);
        var change = Change(windowsPull, SyncChangeEntityType.PendingEventDecision, pendingEventId);
        change.Operation.Should().Be(SyncOperation.Upsert);
        change.SourceDeviceId.Should().Be(phone.DeviceId);

        var decision = Payload<PendingEventDecisionPayload>(change);
        decision.PendingEventId.Should().Be(pendingEventId);
        decision.Decision.Should().Be(PendingEventDecision.Approve);
        decision.DecidedByDeviceId.Should().Be(phone.DeviceId, "Windows keeps this as the audit trail of who reviewed what");
        decision.DecidedAtUtc.Should().Be(UpdatedAt);
        decision.Corrections!.Title.Should().Be("Camping outside Green River");
        decision.Corrections.DatePrecision.Should().Be("day");

        // A rejection is the same shape with the other verdict.
        var rejection = await SyncApiFixture.PushAsync(
            phoneClient,
            DecisionEntry(2, NewId(), phone.DeviceId, payload => payload.Decision = PendingEventDecision.Reject));
        rejection.Results.Should().ContainSingle().Which.Accepted.Should().BeTrue();

        // The deciding phone does not pull back its own verdict.
        var phonePull = await SyncApiFixture.PullAsync(phoneClient, phoneBaseline);
        phonePull.Changes.Should().NotContain(c => c.EntityType == SyncChangeEntityType.PendingEventDecision);
    }

    [Fact]
    public async Task Projection_PushedFromACompanion_IsRefusedAsNotPermitted()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var windowsBaseline = await SyncApiFixture.GetCursorAsync(windowsClient);
        var eventId = NewId();
        var eraId = NewId();
        var personId = NewId();
        var pendingEventId = NewId();

        // A companion holds a perfectly good token — it needs one to upload
        // captures — so nothing but this rule stops it writing a memory that
        // never happened into a timeline every other device renders.
        var push = await SyncApiFixture.PushAsync(
            phoneClient,
            EventEntry(1, eventId, payload => payload.Title = "Dinner that never happened"),
            EraEntry(2, eraId),
            PersonEntry(3, personId),
            PendingEventEntry(4, pendingEventId),
            DeleteEntry(5, SyncChangeEntityType.Event, NewId()));
        push.Results.Should().HaveCount(5);
        push.Results.Should().OnlyContain(r => !r.Accepted && r.ServerChangeId == null);
        push.Results.Should().OnlyContain(r => r.Error!.StartsWith("change_not_permitted"));

        // Not "unauthorized": the token is fine, and a client that re-registered
        // on the strength of this error would get the same answer again.
        push.Results.Should().OnlyContain(r => !r.Error!.StartsWith("unauthorized"));

        // Nothing reached the feed, including the forged deletion.
        var pull = await SyncApiFixture.PullAsync(windowsClient, windowsBaseline);
        pull.Changes.Should().NotContain(c =>
            c.EntityId == eventId || c.EntityId == eraId
            || c.EntityId == personId || c.EntityId == pendingEventId);

        // The same entries from the machine that owns the archive are accepted,
        // so what was refused was the publisher, not the payload.
        var fromWindows = await SyncApiFixture.PushAsync(
            windowsClient, EventEntry(1, eventId), EraEntry(2, eraId));
        fromWindows.Results.Should().OnlyContain(r => r.Accepted);
    }

    [Fact]
    public async Task Decision_WithAnUnknownVerdict_IsRejectedLikeAnUnknownPlatform()
    {
        var (phone, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var windowsBaseline = await SyncApiFixture.GetCursorAsync(windowsClient);
        var pendingEventId = NewId();

        // The shape a client already handles for an unknown device platform.
        using var unauthenticated = _fixture.CreateClient();
        var badPlatform = await unauthenticated.PostAsJsonAsync(
            "/api/v1/devices/register",
            new DeviceRegisterRequest
            {
                PairingCode = SyncApiFixture.PairingCode,
                Platform = "toaster",
                DisplayName = "Toaster",
            },
            SyncApiFixture.Json);
        badPlatform.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var platformError = await SyncApiFixture.ReadApiErrorAsync(badPlatform);

        // A verdict outside the vocabulary is refused rather than quietly
        // becoming the default — the default is "approve", and guessing it would
        // write an unreviewed event into the timeline.
        var push = await SyncApiFixture.PushAsync(
            phoneClient,
            DecisionEntry(1, pendingEventId, phone.DeviceId, payload => payload.Decision = "maybe"),
            DecisionEntry(2, pendingEventId, phone.DeviceId, payload => payload.Decision = "Approve"),
            DecisionEntry(3, pendingEventId, phone.DeviceId, payload => payload.Decision = null!));
        push.Results.Should().HaveCount(3);
        push.Results.Should().OnlyContain(r => !r.Accepted && r.ServerChangeId == null);
        push.Results.Should().OnlyContain(r => r.Error!.StartsWith(platformError.Code));
        push.Results[0].Error.Should().Contain("approve, reject");

        var pull = await SyncApiFixture.PullAsync(windowsClient, windowsBaseline);
        pull.Changes.Should().NotContain(c => c.EntityId == pendingEventId);
    }

    [Fact]
    public async Task Decision_AttributedToAnotherDevice_IsRefused()
    {
        var (phoneA, phoneAClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (phoneB, _) = await _fixture.RegisterClientAsync("ios", "iPad");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var windowsBaseline = await SyncApiFixture.GetCursorAsync(windowsClient);
        var pendingEventId = NewId();

        // Same owner, same review, two entries that differ only in whose name is
        // on the verdict. One bad entry never fails the batch, so the honest one
        // still lands.
        var push = await SyncApiFixture.PushAsync(
            phoneAClient,
            DecisionEntry(1, pendingEventId, phoneB.DeviceId),
            DecisionEntry(2, pendingEventId, phoneA.DeviceId));
        push.Results.Should().HaveCount(2);
        push.Results[0].Accepted.Should().BeFalse();
        push.Results[0].Error.Should().StartWith("change_not_permitted").And.Contain("decidedByDeviceId");
        push.Results[0].ServerChangeId.Should().BeNull();
        push.Results[1].Accepted.Should().BeTrue();

        // Windows sees one verdict, signed by the device that actually decided.
        var pull = await SyncApiFixture.PullAsync(windowsClient, windowsBaseline);
        var change = Change(pull, SyncChangeEntityType.PendingEventDecision, pendingEventId);
        Payload<PendingEventDecisionPayload>(change).DecidedByDeviceId.Should().Be(phoneA.DeviceId);
    }

    [Fact]
    public async Task Delete_TombstonesAProjection_ButMayNotRetractADecision()
    {
        var (phone, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);
        var eventId = NewId();
        var eraId = NewId();
        var personId = NewId();
        var pendingEventId = NewId();

        await SyncApiFixture.PushAsync(
            windowsClient,
            EventEntry(1, eventId),
            EraEntry(2, eraId),
            PersonEntry(3, personId),
            PendingEventEntry(4, pendingEventId));

        // A deleted event must be able to tombstone: the companion has already
        // drawn the row and needs to be told to drop it. The tombstone carries no
        // payload, because a deleted event has nothing left to describe.
        var deletes = await SyncApiFixture.PushAsync(
            windowsClient,
            DeleteEntry(5, SyncChangeEntityType.Event, eventId),
            DeleteEntry(6, SyncChangeEntityType.Era, eraId),
            DeleteEntry(7, SyncChangeEntityType.Person, personId),
            DeleteEntry(8, SyncChangeEntityType.PendingEvent, pendingEventId));
        deletes.Results.Should().HaveCount(4);
        deletes.Results.Should().OnlyContain(r => r.Accepted && !r.Duplicate);

        // The tombstone arrives under the same entity ID as the upsert, so the
        // consumer removes the row it created rather than accumulating a ghost.
        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        foreach (var (entityType, entityId) in new[]
                 {
                     (SyncChangeEntityType.Event, eventId),
                     (SyncChangeEntityType.Era, eraId),
                     (SyncChangeEntityType.Person, personId),
                     (SyncChangeEntityType.PendingEvent, pendingEventId),
                 })
        {
            var changes = Changes(pull, entityType, entityId);
            changes.Select(c => c.Operation).Should().Equal(SyncOperation.Upsert, SyncOperation.Delete);
            changes[^1].PayloadJson.Should().BeNull();
        }

        // A verdict is a fact about a review that happened. Retracting one has no
        // meaning, and accepting a retraction would reach back into a review
        // Windows may already have completed — from either kind of device.
        var retractions = await SyncApiFixture.PushAsync(
            windowsClient, DeleteEntry(9, SyncChangeEntityType.PendingEventDecision, pendingEventId));
        var fromPhone = await SyncApiFixture.PushAsync(
            phoneClient, DeleteEntry(1, SyncChangeEntityType.PendingEventDecision, pendingEventId));
        foreach (var refused in retractions.Results.Concat(fromPhone.Results))
        {
            refused.Accepted.Should().BeFalse();
            refused.Error.Should().StartWith("validation_error").And.Contain("append-only");
            refused.ServerChangeId.Should().BeNull();
        }

        // Changing one's mind is a new decision, which is what an upsert is.
        var again = await SyncApiFixture.PushAsync(
            phoneClient,
            DecisionEntry(2, pendingEventId, phone.DeviceId, payload => payload.Decision = PendingEventDecision.Reject));
        again.Results.Should().ContainSingle().Which.Accepted.Should().BeTrue();

        var afterRetraction = await SyncApiFixture.PullAsync(phoneClient, baseline);
        Changes(afterRetraction, SyncChangeEntityType.PendingEventDecision, pendingEventId)
            .Should().BeEmpty("the phone published the only surviving decision itself");
    }

    [Fact]
    public async Task Projection_MalformedPayload_IsRejectedPerEntryAndAppendsNothing()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);
        var eventId = NewId();

        var missingPayload = EventEntry(1, eventId);
        missingPayload.PayloadJson = null;
        var unparseable = EventEntry(2, eventId);
        unparseable.PayloadJson = "{ not json";
        var mismatchedId = EventEntry(3, eventId);
        mismatchedId.EntityId = NewId();

        // Entity IDs are pinned to one spelling: an upsert of {A1B2...} followed
        // by a delete of a1b2... would read as two rows, and the deletion would
        // tombstone nothing.
        var uppercased = EventEntry(4, eventId.ToUpperInvariant());
        var braced = EventEntry(5, $"{{{eventId}}}");

        var badPrecision = EventEntry(6, eventId, payload => payload.DatePrecision = "summer-ish");
        var negativeMedia = EventEntry(7, eventId, payload => payload.MediaCount = -1);
        var badColor = EraEntry(8, eventId, payload => payload.ColorCode = "cornflower");
        var negativeEvents = PersonEntry(9, eventId, payload => payload.EventCount = -1);
        var badMerge = PersonEntry(10, eventId, payload => payload.MergedIntoId = "the-other-one");
        var impossibleConfidence = PendingEventEntry(11, eventId, payload => payload.ConfidenceScore = 1.4);

        // Truncation is the publisher's call, here as for a capture status
        // preview — the same recording, the same §14.5 bound.
        var overLongPreview = PendingEventEntry(12, eventId, payload => payload.TranscriptPreview =
            new string('t', CaptureStatusChangePayload.TranscriptPreviewMaxChars + 1));

        var push = await SyncApiFixture.PushAsync(
            windowsClient,
            missingPayload, unparseable, mismatchedId, uppercased, braced, badPrecision,
            negativeMedia, badColor, negativeEvents, badMerge, impossibleConfidence, overLongPreview);
        push.Results.Should().HaveCount(12);
        push.Results.Should().OnlyContain(r => !r.Accepted && r.Error!.StartsWith("validation_error"));
        push.Results[3].Error.Should().Contain("canonical");
        push.Results[5].Error.Should().Contain("datePrecision");
        push.Results[11].Error.Should().Contain("transcriptPreview");

        // A preview of exactly the bound is accepted, so a publisher that
        // measured correctly is not refused for being exact — and an era colour
        // carrying an alpha channel still lands, because losing the background
        // of a period of someone's life over one is a worse outcome than a
        // colour a consumer has to widen its parser for.
        var accepted = await SyncApiFixture.PushAsync(
            windowsClient,
            PendingEventEntry(13, NewId(), payload => payload.TranscriptPreview =
                new string('t', CaptureStatusChangePayload.TranscriptPreviewMaxChars)),
            EraEntry(14, NewId(), payload => payload.ColorCode = "#FF3B7DD8"));
        accepted.Results.Should().HaveCount(2);
        accepted.Results.Should().OnlyContain(r => r.Accepted);

        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        pull.Changes.Should().NotContain(c => c.EntityId == eventId || c.EntityId == mismatchedId.EntityId);
    }

    [Fact]
    public async Task Decision_MalformedPayload_IsRejectedPerEntryAndAppendsNothing()
    {
        var (phone, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var baseline = await SyncApiFixture.GetCursorAsync(windowsClient);
        var pendingEventId = NewId();

        var missingPayload = DecisionEntry(1, pendingEventId, phone.DeviceId);
        missingPayload.PayloadJson = null;
        var unparseable = DecisionEntry(2, pendingEventId, phone.DeviceId);
        unparseable.PayloadJson = "{ not json";
        var noPendingEventId = DecisionEntry(3, pendingEventId, phone.DeviceId, payload =>
            payload.PendingEventId = string.Empty);
        var mismatchedId = DecisionEntry(4, pendingEventId, phone.DeviceId);
        mismatchedId.EntityId = NewId();
        var unsignedDecision = DecisionEntry(5, pendingEventId, decidedByDeviceId: string.Empty);

        // Corrections ride the verdict, but the field that changes how a date is
        // read is still held to the vocabulary — this is a companion's write.
        var badCorrection = DecisionEntry(6, pendingEventId, phone.DeviceId, payload =>
            payload.Corrections = new PendingEventCorrections { DatePrecision = "thereabouts" });

        var push = await SyncApiFixture.PushAsync(
            phoneClient, missingPayload, unparseable, noPendingEventId, mismatchedId, unsignedDecision, badCorrection);
        push.Results.Should().HaveCount(6);
        push.Results.Should().OnlyContain(r => !r.Accepted && r.ServerChangeId == null);
        push.Results[2].Error.Should().StartWith("validation_error").And.Contain("pendingEventId");
        push.Results[4].Error.Should().StartWith("change_not_permitted");
        push.Results[5].Error.Should().StartWith("validation_error").And.Contain("corrections.datePrecision");

        var pull = await SyncApiFixture.PullAsync(windowsClient, baseline);
        pull.Changes.Should().NotContain(c => c.EntityType == SyncChangeEntityType.PendingEventDecision);
    }

    [Fact]
    public async Task Projections_AreEchoedToPeersButNeverBackToThePublisher()
    {
        var (phone, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, tabletClient) = await _fixture.RegisterClientAsync("ios", "iPad");
        var (windows, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var phoneBaseline = await SyncApiFixture.GetCursorAsync(phoneClient);
        var tabletBaseline = await SyncApiFixture.GetCursorAsync(tabletClient);
        var windowsBaseline = await SyncApiFixture.GetCursorAsync(windowsClient);
        var eraId = NewId();
        var personId = NewId();
        var pendingEventId = NewId();

        await SyncApiFixture.PushAsync(windowsClient, EraEntry(1, eraId), PersonEntry(2, personId));

        // Both companions get the projections; the publisher does not get its
        // own back and so never re-applies what it already holds.
        foreach (var (client, cursor) in new[] { (phoneClient, phoneBaseline), (tabletClient, tabletBaseline) })
        {
            var peerPull = await SyncApiFixture.PullAsync(client, cursor);
            peerPull.Changes.Should().Contain(c => c.EntityType == SyncChangeEntityType.Era && c.EntityId == eraId);
            peerPull.Changes.Should().Contain(c => c.EntityType == SyncChangeEntityType.Person && c.EntityId == personId);
            peerPull.Changes.Should().OnlyContain(c => c.SourceDeviceId == windows.DeviceId);
        }

        var ownPull = await SyncApiFixture.PullAsync(windowsClient, windowsBaseline);
        ownPull.Changes.Should().NotContain(c => c.EntityId == eraId || c.EntityId == personId);

        // The same holds for the verdict travelling the other way: the deciding
        // phone does not receive it, the archive and the other companion do.
        var afterProjections = await SyncApiFixture.GetCursorAsync(phoneClient);
        await SyncApiFixture.PushAsync(phoneClient, DecisionEntry(1, pendingEventId, phone.DeviceId));

        var decidingPull = await SyncApiFixture.PullAsync(phoneClient, afterProjections);
        decidingPull.Changes.Should().NotContain(c => c.EntityId == pendingEventId);

        foreach (var (client, cursor) in new[] { (windowsClient, windowsBaseline), (tabletClient, tabletBaseline) })
        {
            var pull = await SyncApiFixture.PullAsync(client, cursor);
            pull.Changes.Should().ContainSingle(c =>
                c.EntityType == SyncChangeEntityType.PendingEventDecision && c.EntityId == pendingEventId);
        }
    }

    /// <summary>A fresh entity ID in the canonical lowercase "D" form the service pins.</summary>
    private static string NewId() => Guid.NewGuid().ToString("D");

    /// <summary>The single change of one entity type for one entity ID.</summary>
    private static SyncChangeDto Change(SyncPullResponse pull, string entityType, string entityId) =>
        pull.Changes.Should().ContainSingle(c => c.EntityType == entityType && c.EntityId == entityId).Which;

    /// <summary>Every change of one entity type for one entity ID, in cursor order.</summary>
    private static List<SyncChangeDto> Changes(SyncPullResponse pull, string entityType, string entityId) =>
        pull.Changes
            .Where(c => c.EntityType == entityType && c.EntityId == entityId)
            .OrderBy(c => c.ChangeId)
            .ToList();

    private static TPayload Payload<TPayload>(SyncChangeDto change) =>
        JsonSerializer.Deserialize<TPayload>(change.PayloadJson!, SyncApiFixture.Json)!;

    private static SyncPushEntry EventEntry(
        long clientSequence, string eventId, Action<EventProjectionPayload>? customize = null)
    {
        var payload = new EventProjectionPayload
        {
            EventId = eventId,
            Title = "Camp at Green River",
            Description = "Pulled off the highway and slept by the water.",
            StartDate = EventStart,
            DatePrecision = "season",
            DisplayDate = "Summer 2024",
            Category = "travel",
            UpdatedAtUtc = UpdatedAt,
        };
        customize?.Invoke(payload);
        return Entry(clientSequence, SyncChangeEntityType.Event, eventId, payload);
    }

    private static SyncPushEntry EraEntry(
        long clientSequence, string eraId, Action<EraProjectionPayload>? customize = null)
    {
        var payload = new EraProjectionPayload
        {
            EraId = eraId,
            Name = "Denver years",
            Subtitle = "Apartment on 16th",
            StartDate = new DateTime(2019, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ColorCode = "#3B7DD8",
            DisplayOrder = 2,
            UpdatedAtUtc = UpdatedAt,
        };
        customize?.Invoke(payload);
        return Entry(clientSequence, SyncChangeEntityType.Era, eraId, payload);
    }

    private static SyncPushEntry PersonEntry(
        long clientSequence, string personId, Action<PersonProjectionPayload>? customize = null)
    {
        var payload = new PersonProjectionPayload
        {
            PersonId = personId,
            Name = "Dana Reyes",
            Nickname = "Dana",
            Relationship = "sister",
            IsFavorite = true,
            EventCount = 12,
            UpdatedAtUtc = UpdatedAt,
        };
        customize?.Invoke(payload);
        return Entry(clientSequence, SyncChangeEntityType.Person, personId, payload);
    }

    private static SyncPushEntry PendingEventEntry(
        long clientSequence, string pendingEventId, Action<PendingEventProjectionPayload>? customize = null)
    {
        var payload = new PendingEventProjectionPayload
        {
            PendingEventId = pendingEventId,
            CaptureId = NewId(),
            Title = "Camping outside Green River",
            StartDate = EventStart,
            DatePrecision = "day",
            DisplayDate = "2 June 2024",
            ConfidenceScore = 0.82,
            Tags = ["roadtrip"],
            PeopleNames = ["Dana"],
            TranscriptPreview = "We camped by the river outside Green River.",
            UpdatedAtUtc = UpdatedAt,
        };
        customize?.Invoke(payload);
        return Entry(clientSequence, SyncChangeEntityType.PendingEvent, pendingEventId, payload);
    }

    /// <summary>
    /// A decision entry whose `decision` field is absent entirely must be
    /// refused, not read as an approval.
    ///
    /// This is a regression guard on the contract rather than on the service.
    /// PendingEventDecisionPayload once defaulted Decision to "approve", so a
    /// truncated or partially-written payload deserialized into a verdict that
    /// writes an extracted event into the user's timeline. The failure mode of
    /// a missing field must never be a write to the archive.
    /// </summary>
    [Fact]
    public async Task PushRejectsADecisionWithNoVerdict()
    {
        var (phone, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var pendingEventId = NewId();

        // Serialize a decision and physically remove the field, which is what a
        // client built against an older contract would actually send.
        var entry = DecisionEntry(1, pendingEventId, phone.DeviceId);
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            entry.PayloadJson!, SyncApiFixture.Json)!;
        body.Remove("decision");
        entry.PayloadJson = JsonSerializer.Serialize(body, SyncApiFixture.Json);

        var push = await SyncApiFixture.PushAsync(phoneClient, entry);

        push.Results.Should().HaveCount(1);
        push.Results[0].Accepted.Should().BeFalse(
            "an omitted verdict must not be read as approval");
        push.Results[0].Error.Should().StartWith("validation_error");
    }

    /// <summary>
    /// Builds a decision entry the way a companion's outbox does: entityId equal
    /// to the pending event being decided, upsert operation, the deciding
    /// device's own ID in the payload.
    /// </summary>
    private static SyncPushEntry DecisionEntry(
        long clientSequence,
        string pendingEventId,
        string decidedByDeviceId,
        Action<PendingEventDecisionPayload>? customize = null)
    {
        var payload = new PendingEventDecisionPayload
        {
            PendingEventId = pendingEventId,
            Decision = PendingEventDecision.Approve,
            DecidedByDeviceId = decidedByDeviceId,
            DecidedAtUtc = UpdatedAt,
        };
        customize?.Invoke(payload);
        return Entry(clientSequence, SyncChangeEntityType.PendingEventDecision, pendingEventId, payload);
    }

    /// <summary>A tombstone: the entity ID of the row to drop, and no payload.</summary>
    private static SyncPushEntry DeleteEntry(long clientSequence, string entityType, string entityId) =>
        Entry(clientSequence, entityType, entityId, payload: null, SyncOperation.Delete);

    private static SyncPushEntry Entry(
        long clientSequence,
        string entityType,
        string entityId,
        object? payload,
        string operation = SyncOperation.Upsert) => new()
    {
        ClientSequence = clientSequence,
        EntityType = entityType,
        EntityId = entityId,
        Operation = operation,
        PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload, SyncApiFixture.Json),
        ChangedAtUtc = DateTime.UtcNow,
    };
}
