using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MemoryTimeline.SyncContracts;
using Xunit;

namespace MemoryTimeline.SyncApi.Tests;

/// <summary>
/// The Phase 3 status handoff against the real host (design §19 Phase 3):
/// Windows publishes capture_status changes into its outbox, the service
/// validates and persists the latest status per capture, and the capture's
/// originating phone pulls the whole lifecycle without opening Windows.
/// Rejections are per-entry (one bad entry never fails the batch) and leave
/// nothing behind — in particular the service never truncates an over-long
/// transcript preview on the publisher's behalf. Tests share one host per
/// class, so each takes a per-device baseline cursor before acting.
/// </summary>
public class CaptureStatusFlowTests : IClassFixture<SyncApiFixture>
{
    /// <summary>Fixed publisher timestamp, so payload round trips can be asserted exactly.</summary>
    private static readonly DateTime UpdatedAt = new(2026, 8, 7, 14, 30, 0, DateTimeKind.Utc);

    private readonly SyncApiFixture _fixture;

    public CaptureStatusFlowTests(SyncApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CaptureStatus_PushedFromWindows_ReachesOriginatingPhone()
    {
        var (phone, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (windows, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        // A preview of exactly the contract maximum is accepted.
        var preview = new string('t', CaptureStatusChangePayload.TranscriptPreviewMaxChars);
        var push = await SyncApiFixture.PushAsync(
            windowsClient,
            StatusEntry(1, capture.CaptureId, SyncCaptureStatus.ReviewReady, payload =>
            {
                payload.ProcessingStage = "review_ready";
                payload.TranscriptAvailable = true;
                payload.TranscriptPreview = preview;
                payload.TranscriptCharCount = 4321;
                payload.PendingEventCount = 3;
                payload.ApprovedEventCount = 1;
            }));
        var result = push.Results.Should().ContainSingle().Which;
        result.Accepted.Should().BeTrue();
        result.Duplicate.Should().BeFalse();
        result.ServerChangeId.Should().NotBeNull();

        // The change reaches the capture's originating device: the publisher is
        // Windows, so echo suppression cannot swallow it on the phone.
        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        var change = pull.Changes.Should()
            .ContainSingle(c => c.EntityType == SyncChangeEntityType.CaptureStatus).Which;
        change.ChangeId.Should().Be(result.ServerChangeId!.Value);
        change.EntityId.Should().Be(capture.CaptureId);
        change.Operation.Should().Be(SyncOperation.Upsert);
        change.Revision.Should().Be(1);
        change.SourceDeviceId.Should().Be(windows.DeviceId);

        var payload = JsonSerializer.Deserialize<CaptureStatusChangePayload>(
            change.PayloadJson!, SyncApiFixture.Json)!;
        payload.CaptureId.Should().Be(capture.CaptureId);
        payload.Status.Should().Be(SyncCaptureStatus.ReviewReady);
        payload.ProcessingStage.Should().Be("review_ready");
        payload.UpdatedAtUtc.Should().Be(UpdatedAt);
        payload.TranscriptAvailable.Should().BeTrue();
        payload.TranscriptPreview.Should().Be(preview);
        payload.TranscriptCharCount.Should().Be(4321);
        payload.PendingEventCount.Should().Be(3);
        payload.ApprovedEventCount.Should().Be(1);
        payload.FailureReason.Should().BeNull();

        // The capture read model carries the same lifecycle state.
        var updated = await phoneClient.GetFromJsonAsync<CaptureResponse>(
            $"/api/v1/captures/{capture.CaptureId}", SyncApiFixture.Json);
        updated!.Status.Should().Be(SyncCaptureStatus.ReviewReady);
        updated.SourceDeviceId.Should().Be(phone.DeviceId);
    }

    [Fact]
    public async Task CaptureStatus_ReplayedClientSequence_AppendsOneChangeOnly()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        var entry = StatusEntry(7, capture.CaptureId, SyncCaptureStatus.Processing, payload =>
            payload.ProcessingStage = "transcribing");

        var first = await SyncApiFixture.PushAsync(windowsClient, entry);
        var firstResult = first.Results.Should().ContainSingle().Which;
        firstResult.Accepted.Should().BeTrue();
        firstResult.Duplicate.Should().BeFalse();

        var second = await SyncApiFixture.PushAsync(windowsClient, entry);
        var secondResult = second.Results.Should().ContainSingle().Which;
        secondResult.Accepted.Should().BeTrue();
        secondResult.Duplicate.Should().BeTrue();
        secondResult.ServerChangeId.Should().Be(firstResult.ServerChangeId);

        // The replay neither appended a second change nor bumped the revision.
        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        var change = pull.Changes.Should()
            .ContainSingle(c => c.EntityType == SyncChangeEntityType.CaptureStatus).Which;
        change.Revision.Should().Be(1);
    }

    [Fact]
    public async Task CaptureStatus_SeveralUpdatesForOneCapture_ArriveInCursorOrder()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        var stages = new[]
        {
            (Status: SyncCaptureStatus.Processing, Stage: "transcribing"),
            (Status: SyncCaptureStatus.Processing, Stage: "extracting"),
            (Status: SyncCaptureStatus.ReviewReady, Stage: "review_ready"),
            (Status: SyncCaptureStatus.Completed, Stage: "completed"),
        };
        var entries = stages
            .Select((s, i) => StatusEntry(i + 1, capture.CaptureId, s.Status, payload => payload.ProcessingStage = s.Stage))
            .ToArray();

        // Two entries per drain: statuses accumulate both within one push batch
        // and across pushes.
        foreach (var batch in new[] { entries[..2], entries[2..] })
        {
            var push = await SyncApiFixture.PushAsync(windowsClient, batch);
            push.Results.Should().OnlyContain(r => r.Accepted && !r.Duplicate);
        }

        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        var changes = pull.Changes
            .Where(c => c.EntityType == SyncChangeEntityType.CaptureStatus && c.EntityId == capture.CaptureId)
            .ToList();
        changes.Should().HaveCount(stages.Length);
        changes.Select(c => c.ChangeId).Should().BeInAscendingOrder();
        changes.Select(c => c.Revision).Should().Equal(1, 2, 3, 4);
        changes
            .Select(c => JsonSerializer.Deserialize<CaptureStatusChangePayload>(
                c.PayloadJson!, SyncApiFixture.Json)!.ProcessingStage)
            .Should().Equal(stages.Select(s => s.Stage));

        // Latest wins on the read model; a stage-only advance did not change it.
        var updated = await phoneClient.GetFromJsonAsync<CaptureResponse>(
            $"/api/v1/captures/{capture.CaptureId}", SyncApiFixture.Json);
        updated!.Status.Should().Be(SyncCaptureStatus.Completed);
    }

    [Fact]
    public async Task CaptureStatus_OlderStatusRePushedAfterNewerOnes_IsAcceptedButIgnored()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        // The publisher walks the capture to completion...
        var processing = StatusEntry(1, capture.CaptureId, SyncCaptureStatus.Processing, payload =>
        {
            payload.ProcessingStage = "transcribing";
            payload.UpdatedAtUtc = UpdatedAt;
        });
        await SyncApiFixture.PushAsync(windowsClient, processing);
        await SyncApiFixture.PushAsync(
            windowsClient,
            StatusEntry(2, capture.CaptureId, SyncCaptureStatus.Completed, payload =>
            {
                payload.ProcessingStage = "completed";
                payload.UpdatedAtUtc = UpdatedAt.AddMinutes(2);
                payload.ApprovedEventCount = 2;
            }));
        var afterCompleted = await SyncApiFixture.PullAsync(phoneClient, baseline);
        var completedChangeCount = CaptureStatusChanges(afterCompleted, capture.CaptureId).Count;

        // ...then re-pushes the older entry it could not deliver first time
        // round (rejected entries stay pending in the local outbox), under a
        // client sequence the server has no receipt for.
        processing.ClientSequence = 99;
        var push = await SyncApiFixture.PushAsync(windowsClient, processing);
        var result = push.Results.Should().ContainSingle().Which;
        result.Accepted.Should().BeTrue("a stale entry must be consumed, not left to be re-pushed forever");
        result.ServerChangeId.Should().BeNull();

        // No change row was appended and the projection kept the newer state.
        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        var changes = CaptureStatusChanges(pull, capture.CaptureId);
        changes.Should().HaveCount(completedChangeCount);
        var latest = Payload(changes[^1]);
        latest.Status.Should().Be(SyncCaptureStatus.Completed);
        latest.ProcessingStage.Should().Be("completed");
        latest.ApprovedEventCount.Should().Be(2);

        var updated = await phoneClient.GetFromJsonAsync<CaptureResponse>(
            $"/api/v1/captures/{capture.CaptureId}", SyncApiFixture.Json);
        updated!.Status.Should().Be(SyncCaptureStatus.Completed);
    }

    [Fact]
    public async Task CaptureStatus_SameStatusReplayedUnderANewSequence_AppendsNothing()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        var entry = StatusEntry(1, capture.CaptureId, SyncCaptureStatus.Processing, payload =>
            payload.ProcessingStage = "transcribing");
        await SyncApiFixture.PushAsync(windowsClient, entry);

        // Same projection, new client sequence: the receipt cannot dedupe it,
        // so the ordering guard has to.
        entry.ClientSequence = 2;
        var push = await SyncApiFixture.PushAsync(windowsClient, entry);
        var result = push.Results.Should().ContainSingle().Which;
        result.Accepted.Should().BeTrue();
        result.ServerChangeId.Should().BeNull();

        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        var change = CaptureStatusChanges(pull, capture.CaptureId).Should().ContainSingle().Which;
        change.Revision.Should().Be(1);
    }

    [Fact]
    public async Task CaptureStatus_FailedAfterCompleted_IsRecordedAndCanBeAdvancedPast()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        await SyncApiFixture.PushAsync(
            windowsClient,
            StatusEntry(1, capture.CaptureId, SyncCaptureStatus.Completed, payload =>
            {
                payload.ProcessingStage = "completed";
                payload.UpdatedAtUtc = UpdatedAt;
            }));

        // A failure is off the ladder: it must always be recordable, even from
        // completed (the user re-ran the capture on Windows and it broke).
        var failure = await SyncApiFixture.PushAsync(
            windowsClient,
            StatusEntry(2, capture.CaptureId, SyncCaptureStatus.Failed, payload =>
            {
                payload.ProcessingStage = "failed_retryable";
                payload.UpdatedAtUtc = UpdatedAt.AddMinutes(1);
                payload.FailureReason = "Processing failed on the Windows machine; it can be retried there.";
                payload.FailureRetryable = true;
            }));
        failure.Results.Should().ContainSingle().Which.Accepted.Should().BeTrue();

        // And a later real advance must still get past the stored failure.
        var recovered = await SyncApiFixture.PushAsync(
            windowsClient,
            StatusEntry(3, capture.CaptureId, SyncCaptureStatus.ReviewReady, payload =>
            {
                payload.ProcessingStage = "review_ready";
                payload.UpdatedAtUtc = UpdatedAt.AddMinutes(2);
                payload.PendingEventCount = 1;
            }));
        recovered.Results.Should().ContainSingle().Which.Accepted.Should().BeTrue();

        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        var changes = CaptureStatusChanges(pull, capture.CaptureId);
        changes.Select(c => Payload(c).Status).Should().Equal(
            SyncCaptureStatus.Completed, SyncCaptureStatus.Failed, SyncCaptureStatus.ReviewReady);

        var updated = await phoneClient.GetFromJsonAsync<CaptureResponse>(
            $"/api/v1/captures/{capture.CaptureId}", SyncApiFixture.Json);
        updated!.Status.Should().Be(SyncCaptureStatus.ReviewReady);
    }

    [Fact]
    public async Task CaptureStatus_AdvanceAfterAStaleReplay_StillApplies()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        await SyncApiFixture.PushAsync(
            windowsClient,
            StatusEntry(1, capture.CaptureId, SyncCaptureStatus.ReviewReady, payload =>
            {
                payload.ProcessingStage = "review_ready";
                payload.UpdatedAtUtc = UpdatedAt.AddMinutes(1);
                payload.PendingEventCount = 2;
            }));

        // The guard must not latch: ignoring a stale entry cannot stop the next
        // genuine advance from landing.
        await SyncApiFixture.PushAsync(
            windowsClient,
            StatusEntry(2, capture.CaptureId, SyncCaptureStatus.Received, payload =>
            {
                payload.ProcessingStage = "verifying";
                payload.UpdatedAtUtc = UpdatedAt;
            }));
        await SyncApiFixture.PushAsync(
            windowsClient,
            StatusEntry(3, capture.CaptureId, SyncCaptureStatus.Completed, payload =>
            {
                payload.ProcessingStage = "completed";
                payload.UpdatedAtUtc = UpdatedAt.AddMinutes(5);
                payload.ApprovedEventCount = 2;
            }));

        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        var changes = CaptureStatusChanges(pull, capture.CaptureId);
        changes.Select(c => Payload(c).Status).Should().Equal(
            SyncCaptureStatus.ReviewReady, SyncCaptureStatus.Completed);
        changes.Select(c => c.Revision).Should().Equal(1, 2);
        Payload(changes[^1]).ApprovedEventCount.Should().Be(2);
    }

    [Fact]
    public async Task CaptureStatus_PushedFromPhone_IsNotEchoedBackToIt()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var phoneBaseline = await SyncApiFixture.GetCursorAsync(phoneClient);
        var windowsBaseline = await SyncApiFixture.GetCursorAsync(windowsClient);

        // Publishers are not gated by platform — whoever pushes is the change's
        // source device and never receives it back.
        var push = await SyncApiFixture.PushAsync(
            phoneClient, StatusEntry(1, capture.CaptureId, SyncCaptureStatus.Uploading));
        push.Results.Should().ContainSingle().Which.Accepted.Should().BeTrue();

        var ownPull = await SyncApiFixture.PullAsync(phoneClient, phoneBaseline);
        ownPull.Changes.Should().NotContain(c => c.EntityType == SyncChangeEntityType.CaptureStatus);

        var otherPull = await SyncApiFixture.PullAsync(windowsClient, windowsBaseline);
        otherPull.Changes.Should().ContainSingle(
            c => c.EntityType == SyncChangeEntityType.CaptureStatus && c.EntityId == capture.CaptureId);
    }

    [Fact]
    public async Task CaptureStatus_UnknownStatus_IsRejectedAndAppendsNothing()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        var push = await SyncApiFixture.PushAsync(
            windowsClient, StatusEntry(1, capture.CaptureId, "half_transcribed"));
        var result = push.Results.Should().ContainSingle().Which;
        result.Accepted.Should().BeFalse();
        result.Error.Should().StartWith("validation_error");
        result.ServerChangeId.Should().BeNull();

        await AssertNothingRecordedAsync(phoneClient, baseline, capture.CaptureId);
    }

    [Fact]
    public async Task CaptureStatus_TranscriptPreviewOverMaximum_IsRejectedNotTruncated()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        var push = await SyncApiFixture.PushAsync(
            windowsClient,
            StatusEntry(1, capture.CaptureId, SyncCaptureStatus.ReviewReady, payload =>
            {
                payload.TranscriptAvailable = true;
                payload.TranscriptPreview =
                    new string('t', CaptureStatusChangePayload.TranscriptPreviewMaxChars + 1);
            }));
        var result = push.Results.Should().ContainSingle().Which;
        result.Accepted.Should().BeFalse();
        result.Error.Should().StartWith("validation_error").And.Contain("transcriptPreview");

        // Truncation is the publisher's job: the service stored nothing.
        await AssertNothingRecordedAsync(phoneClient, baseline, capture.CaptureId);
    }

    [Fact]
    public async Task CaptureStatus_PayloadCaptureIdMismatchingEntityId_IsRejected()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        var entry = StatusEntry(1, capture.CaptureId, SyncCaptureStatus.ReviewReady);
        entry.EntityId = Guid.NewGuid().ToString("D");

        var push = await SyncApiFixture.PushAsync(windowsClient, entry);
        var result = push.Results.Should().ContainSingle().Which;
        result.Accepted.Should().BeFalse();
        result.Error.Should().StartWith("validation_error").And.Contain("captureId");

        await AssertNothingRecordedAsync(phoneClient, baseline, capture.CaptureId);
    }

    [Fact]
    public async Task CaptureStatus_MalformedPayload_IsRejected()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var capture = await SyncApiFixture.CreateCaptureAsync(phoneClient);
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);

        var missingPayload = StatusEntry(1, capture.CaptureId, SyncCaptureStatus.Processing);
        missingPayload.PayloadJson = null;
        var unparseable = StatusEntry(2, capture.CaptureId, SyncCaptureStatus.Processing);
        unparseable.PayloadJson = "{ not json";
        var deleted = StatusEntry(3, capture.CaptureId, SyncCaptureStatus.Processing);
        deleted.Operation = SyncOperation.Delete;

        var push = await SyncApiFixture.PushAsync(windowsClient, missingPayload, unparseable, deleted);
        push.Results.Should().HaveCount(3);
        push.Results.Should().OnlyContain(r => !r.Accepted && r.Error!.StartsWith("validation_error"));

        await AssertNothingRecordedAsync(phoneClient, baseline, capture.CaptureId);
    }

    [Fact]
    public async Task CaptureStatus_ForUnknownCapture_IsRejectedAsCaptureNotFound()
    {
        var (_, phoneClient) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (_, windowsClient) = await _fixture.RegisterClientAsync("windows", "Desktop");
        var baseline = await SyncApiFixture.GetCursorAsync(phoneClient);
        var unknownCaptureId = Guid.NewGuid().ToString("D");

        var push = await SyncApiFixture.PushAsync(
            windowsClient, StatusEntry(1, unknownCaptureId, SyncCaptureStatus.Processing));
        var result = push.Results.Should().ContainSingle().Which;
        result.Accepted.Should().BeFalse();
        result.Error.Should().StartWith("capture_not_found");

        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        pull.Changes.Should().NotContain(c => c.EntityId == unknownCaptureId);
    }

    /// <summary>capture_status changes for one capture, in cursor order.</summary>
    private static List<SyncChangeDto> CaptureStatusChanges(SyncPullResponse pull, string captureId) =>
        pull.Changes
            .Where(c => c.EntityType == SyncChangeEntityType.CaptureStatus && c.EntityId == captureId)
            .OrderBy(c => c.ChangeId)
            .ToList();

    private static CaptureStatusChangePayload Payload(SyncChangeDto change) =>
        JsonSerializer.Deserialize<CaptureStatusChangePayload>(change.PayloadJson!, SyncApiFixture.Json)!;

    /// <summary>
    /// Asserts a rejected entry left no trace: no change for the phone to pull
    /// and no status applied to the capture read model.
    /// </summary>
    private static async Task AssertNothingRecordedAsync(HttpClient phoneClient, long baseline, string captureId)
    {
        var pull = await SyncApiFixture.PullAsync(phoneClient, baseline);
        pull.Changes.Should().NotContain(c => c.EntityType == SyncChangeEntityType.CaptureStatus);

        var capture = await phoneClient.GetFromJsonAsync<CaptureResponse>(
            $"/api/v1/captures/{captureId}", SyncApiFixture.Json);
        capture!.Status.Should().Be(SyncCaptureStatus.Received);
    }

    /// <summary>
    /// Builds a capture_status push entry the way the Windows outbox publisher
    /// does: entityId equal to the payload's captureId, upsert operation,
    /// camelCase payload JSON.
    /// </summary>
    private static SyncPushEntry StatusEntry(
        long clientSequence,
        string captureId,
        string status,
        Action<CaptureStatusChangePayload>? customize = null)
    {
        var payload = new CaptureStatusChangePayload
        {
            CaptureId = captureId,
            Status = status,
            UpdatedAtUtc = UpdatedAt,
        };
        customize?.Invoke(payload);

        return new SyncPushEntry
        {
            ClientSequence = clientSequence,
            EntityType = SyncChangeEntityType.CaptureStatus,
            EntityId = captureId,
            Operation = SyncOperation.Upsert,
            PayloadJson = JsonSerializer.Serialize(payload, SyncApiFixture.Json),
            ChangedAtUtc = DateTime.UtcNow,
        };
    }
}
