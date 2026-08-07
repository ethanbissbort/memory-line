using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MemoryTimeline.SyncContracts;
using Xunit;

namespace MemoryTimeline.SyncApi.Tests;

/// <summary>
/// The Phase 1 exit criterion end to end against the real host: an iOS test
/// client uploads an audio capture (metadata + multi-part artifact) and a
/// Windows client sees it exactly once via /sync/pull — one ingestion-
/// triggering change carrying the audio artifact summary — then downloads
/// byte-identical audio, acks, and pulls nothing further. This class holds a
/// single test so pulling from cursor 0 observes a pristine change log.
/// </summary>
public class EndToEndTests : IClassFixture<SyncApiFixture>
{
    private readonly SyncApiFixture _fixture;

    public EndToEndTests(SyncApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AudioCapture_UploadedFromIos_AppearsExactlyOnceInWindowsPull()
    {
        // Pair both devices.
        var deviceA = await _fixture.RegisterDeviceAsync("ios", "iPhone");
        var deviceB = await _fixture.RegisterDeviceAsync("windows", "Desktop");
        using var clientA = _fixture.CreateClient(deviceA.AccessToken);
        using var clientB = _fixture.CreateClient(deviceB.AccessToken);

        // A creates the capture; the server takes the source device from the
        // access token, not the (spoofed) body field.
        var createRequest = SyncApiFixture.NewCaptureRequest();
        createRequest.SourceDeviceId = Guid.NewGuid().ToString("D");
        var capture = await SyncApiFixture.CreateCaptureAsync(clientA, createRequest);
        capture.CaptureId.Should().Be(createRequest.CaptureId);
        capture.SourceDeviceId.Should().Be(deviceA.DeviceId);
        capture.Status.Should().Be(SyncCaptureStatus.Received);
        capture.ServerRevision.Should().Be(1);

        // A uploads the audio artifact in two parts (100 bytes at a 64-byte part size).
        var content = SyncApiFixture.RandomBytes(100);
        var sha = SyncApiFixture.Sha256Hex(content);
        var artifactId = Guid.NewGuid().ToString("D");
        var initiate = await SyncApiFixture.InitiateArtifactAsync(
            clientA, capture.CaptureId, content, artifactId: artifactId);
        initiate.ArtifactId.Should().Be(artifactId);
        initiate.PartSizeBytes.Should().Be(SyncApiFixture.PartSizeBytes);
        initiate.ExpectedPartCount.Should().Be(2);

        await SyncApiFixture.UploadPartsAsync(clientA, artifactId, content);

        var completeResponse = await SyncApiFixture.CompleteArtifactAsync(
            clientA, artifactId, partCount: 2, totalByteLength: content.Length, sha256: sha);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var completed = await completeResponse.Content.ReadFromJsonAsync<ArtifactCompleteResponse>(
            SyncApiFixture.Json);
        completed!.State.Should().Be(ArtifactState.Complete);
        completed.ByteLength.Should().Be(content.Length);
        completed.Sha256.Should().Be(sha);

        // B pulls from cursor 0: the capture appears with its changes in order,
        // and exactly one change carries the ingestion-triggering audio artifact.
        var pull = await SyncApiFixture.PullAsync(clientB, cursor: 0);
        pull.HasMore.Should().BeFalse();
        pull.Changes.Should().OnlyContain(
            c => c.SourceDeviceId == deviceA.DeviceId,
            "every change in this pristine log originated on device A");

        var captureChanges = pull.Changes.Where(c => c.EntityId == capture.CaptureId).ToList();
        captureChanges.Should().HaveCount(2, "capture create and artifact completion each append one change");
        captureChanges.Should().OnlyContain(c => c.EntityType == SyncChangeEntityType.Capture);
        captureChanges.Should().OnlyContain(c => c.Operation == SyncOperation.Upsert);
        captureChanges[0].Revision.Should().Be(1);
        captureChanges[1].Revision.Should().Be(2);

        var payloads = captureChanges
            .Select(c => JsonSerializer.Deserialize<CaptureChangePayload>(c.PayloadJson!, SyncApiFixture.Json)!)
            .ToList();
        payloads[0].AudioArtifact.Should().BeNull("the create change precedes the audio upload");
        payloads.Count(p => p.AudioArtifact is not null).Should().Be(
            1, "exactly one change may trigger Windows ingestion");

        var ingestion = payloads[1];
        ingestion.CaptureId.Should().Be(capture.CaptureId);
        ingestion.SourceDeviceId.Should().Be(deviceA.DeviceId);
        ingestion.SourcePlatform.Should().Be("ios");
        ingestion.Status.Should().Be(SyncCaptureStatus.Received);
        ingestion.AudioArtifact!.ArtifactId.Should().Be(artifactId);
        ingestion.AudioArtifact.ArtifactType.Should().Be("audio_original");
        ingestion.AudioArtifact.State.Should().Be(ArtifactState.Complete);
        ingestion.AudioArtifact.ByteLength.Should().Be(content.Length);
        ingestion.AudioArtifact.Sha256.Should().Be(sha);

        // Echo suppression: A pulling the same log sees none of its own changes.
        var pullA = await SyncApiFixture.PullAsync(clientA, cursor: 0);
        pullA.Changes.Should().BeEmpty("a device never receives its own changes back");
        pullA.HasMore.Should().BeFalse();

        // B downloads the audio: byte-identical to what A recorded.
        var download = await clientB.GetAsync($"/api/v1/artifacts/{artifactId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("audio/mp4");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(content);

        // B acks and pulls again from its new cursor: nothing arrives twice.
        var ack = await clientB.PostAsJsonAsync(
            "/api/v1/sync/ack", new SyncAckRequest { Cursor = pull.NextCursor }, SyncApiFixture.Json);
        ack.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await SyncApiFixture.PullAsync(clientB, pull.NextCursor);
        second.Changes.Should().BeEmpty("the capture must appear exactly once in Windows review");
        second.NextCursor.Should().Be(pull.NextCursor);
        second.HasMore.Should().BeFalse();
    }
}
