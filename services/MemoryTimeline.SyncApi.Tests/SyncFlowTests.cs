using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MemoryTimeline.SyncContracts;
using Xunit;

namespace MemoryTimeline.SyncApi.Tests;

/// <summary>
/// Change-log synchronization flows against the real host: idempotent capture
/// creation appends no duplicate ingestion-triggering changes, push entries
/// are deduplicated per (device, clientSequence), pushed capture status
/// updates apply server-side and flow to the other device, and pull pages
/// with HasMore and an advancing cursor. Tests share one host per class, so
/// each captures a per-device baseline cursor before acting.
/// </summary>
public class SyncFlowTests : IClassFixture<SyncApiFixture>
{
    private readonly SyncApiFixture _fixture;

    public SyncFlowTests(SyncApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CaptureCreate_ReplayAfterArtifactComplete_AppendsNoExtraChanges()
    {
        var (_, clientA) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, clientB) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var baseline = await SyncApiFixture.GetCursorAsync(clientB);

        // Create the capture and complete its audio artifact.
        var request = SyncApiFixture.NewCaptureRequest();
        var first = await clientA.PostAsJsonAsync("/api/v1/captures", request, SyncApiFixture.Json);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var content = SyncApiFixture.RandomBytes(100);
        var sha = SyncApiFixture.Sha256Hex(content);
        var initiate = await SyncApiFixture.InitiateArtifactAsync(clientA, request.CaptureId, content);
        await SyncApiFixture.UploadPartsAsync(clientA, initiate.ArtifactId, content);
        var complete = await SyncApiFixture.CompleteArtifactAsync(
            clientA, initiate.ArtifactId, partCount: 2, totalByteLength: content.Length, sha256: sha);
        complete.StatusCode.Should().Be(HttpStatusCode.OK);

        // Replaying the identical create is idempotent: 200 with current state.
        var replay = await clientA.PostAsJsonAsync("/api/v1/captures", request, SyncApiFixture.Json);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = await replay.Content.ReadFromJsonAsync<CaptureResponse>(SyncApiFixture.Json);
        replayed!.CaptureId.Should().Be(request.CaptureId);
        replayed.Artifacts.Should().ContainSingle(
            a => a.ArtifactId == initiate.ArtifactId && a.State == ArtifactState.Complete);

        // B sees exactly the create + completion changes — the replay appended
        // nothing, so there is no duplicate ingestion trigger.
        var pull = await SyncApiFixture.PullAsync(clientB, baseline);
        var captureChanges = pull.Changes.Where(c => c.EntityId == request.CaptureId).ToList();
        captureChanges.Should().HaveCount(2, "the replayed create must not append a third change");
        captureChanges
            .Select(c => JsonSerializer.Deserialize<CaptureChangePayload>(c.PayloadJson!, SyncApiFixture.Json)!)
            .Count(p => p.AudioArtifact is not null)
            .Should().Be(1, "only the artifact completion carries the ingestion-triggering audio artifact");
    }

    [Fact]
    public async Task Push_SameClientSequenceTwice_SecondIsDuplicateWithOneChangeRow()
    {
        var (_, clientA) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, clientB) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var baseline = await SyncApiFixture.GetCursorAsync(clientA);

        var entry = new SyncPushEntry
        {
            ClientSequence = 42,
            EntityType = SyncChangeEntityType.Capture,
            EntityId = Guid.NewGuid().ToString("D"),
            Operation = SyncOperation.Upsert,
            ChangedAtUtc = DateTime.UtcNow,
        };

        var first = await SyncApiFixture.PushAsync(clientB, entry);
        var firstResult = first.Results.Should().ContainSingle().Which;
        firstResult.Accepted.Should().BeTrue();
        firstResult.Duplicate.Should().BeFalse();
        firstResult.ServerChangeId.Should().NotBeNull();

        var second = await SyncApiFixture.PushAsync(clientB, entry);
        var secondResult = second.Results.Should().ContainSingle().Which;
        secondResult.Accepted.Should().BeTrue();
        secondResult.Duplicate.Should().BeTrue();
        secondResult.ServerChangeId.Should().Be(firstResult.ServerChangeId);

        // Observable through the other device: exactly one change row exists.
        var pull = await SyncApiFixture.PullAsync(clientA, baseline);
        pull.Changes.Should().ContainSingle(c => c.EntityId == entry.EntityId);
    }

    [Fact]
    public async Task Push_CaptureStatusUpdate_AppliesAndFlowsToOtherDevice()
    {
        var (_, clientA) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (deviceB, clientB) = await _fixture.RegisterClientAsync("windows", "Desktop");

        var capture = await SyncApiFixture.CreateCaptureAsync(clientA);
        var baselineA = await SyncApiFixture.GetCursorAsync(clientA);

        // Windows marks the capture review-ready via its outbox.
        var payloadJson = JsonSerializer.Serialize(
            new CaptureChangePayload
            {
                CaptureId = capture.CaptureId,
                SourceDeviceId = deviceB.DeviceId,
                SourcePlatform = "windows",
                CaptureType = capture.CaptureType,
                CapturedAtUtc = capture.CapturedAtUtc,
                Status = SyncCaptureStatus.ReviewReady,
            },
            SyncApiFixture.Json);
        var push = await SyncApiFixture.PushAsync(clientB, new SyncPushEntry
        {
            ClientSequence = 1,
            EntityType = SyncChangeEntityType.Capture,
            EntityId = capture.CaptureId,
            Operation = SyncOperation.Upsert,
            PayloadJson = payloadJson,
            ChangedAtUtc = DateTime.UtcNow,
        });
        var result = push.Results.Should().ContainSingle().Which;
        result.Accepted.Should().BeTrue();
        result.Duplicate.Should().BeFalse();

        // Echo suppression works both ways: A receives B's change.
        var pull = await SyncApiFixture.PullAsync(clientA, baselineA);
        var change = pull.Changes.Should().ContainSingle(c => c.EntityId == capture.CaptureId).Which;
        change.SourceDeviceId.Should().Be(deviceB.DeviceId);
        var payload = JsonSerializer.Deserialize<CaptureChangePayload>(change.PayloadJson!, SyncApiFixture.Json);
        payload!.Status.Should().Be(SyncCaptureStatus.ReviewReady);

        // The server applied the status and bumped the revision.
        var updated = await clientA.GetFromJsonAsync<CaptureResponse>(
            $"/api/v1/captures/{capture.CaptureId}", SyncApiFixture.Json);
        updated!.Status.Should().Be(SyncCaptureStatus.ReviewReady);
        updated.ServerRevision.Should().Be(2);
    }

    [Fact]
    public async Task Pull_MoreChangesThanLimit_PagesWithHasMoreAndAdvancingCursor()
    {
        var (_, clientA) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, clientB) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var baseline = await SyncApiFixture.GetCursorAsync(clientA);

        var entries = Enumerable.Range(1, 5)
            .Select(sequence => new SyncPushEntry
            {
                ClientSequence = sequence,
                EntityType = SyncChangeEntityType.Capture,
                EntityId = Guid.NewGuid().ToString("D"),
                Operation = SyncOperation.Upsert,
                ChangedAtUtc = DateTime.UtcNow,
            })
            .ToArray();
        var push = await SyncApiFixture.PushAsync(clientB, entries);
        push.Results.Should().HaveCount(5);
        push.Results.Should().OnlyContain(r => r.Accepted && !r.Duplicate);

        var firstPage = await SyncApiFixture.PullAsync(clientA, baseline, limit: 2);
        firstPage.Changes.Should().HaveCount(2);
        firstPage.HasMore.Should().BeTrue();
        firstPage.NextCursor.Should().BeGreaterThan(baseline);
        firstPage.NextCursor.Should().Be(firstPage.Changes[^1].ChangeId);

        var secondPage = await SyncApiFixture.PullAsync(clientA, firstPage.NextCursor, limit: 2);
        secondPage.Changes.Should().HaveCount(2);
        secondPage.HasMore.Should().BeTrue();
        secondPage.NextCursor.Should().BeGreaterThan(firstPage.NextCursor);

        var thirdPage = await SyncApiFixture.PullAsync(clientA, secondPage.NextCursor, limit: 2);
        thirdPage.Changes.Should().HaveCount(1);
        thirdPage.HasMore.Should().BeFalse();

        var all = firstPage.Changes.Concat(secondPage.Changes).Concat(thirdPage.Changes).ToList();
        all.Select(c => c.ChangeId).Should().BeInAscendingOrder();
        all.Select(c => c.EntityId).Should().BeEquivalentTo(
            entries.Select(e => e.EntityId),
            "paging must return every pushed change exactly once");
    }
}
