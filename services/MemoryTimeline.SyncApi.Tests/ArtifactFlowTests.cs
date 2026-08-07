using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MemoryTimeline.SyncContracts;
using Xunit;

namespace MemoryTimeline.SyncApi.Tests;

/// <summary>
/// Artifact integrity and path-safety flows against the real host: completion
/// rejects hash/length mismatches with stable 422 codes and stays retryable
/// (parts survive, a corrected re-upload succeeds), incomplete artifacts are
/// not downloadable, replayed completion is idempotent, and non-GUID artifact
/// IDs or out-of-range part numbers never reach the filesystem.
/// </summary>
public class ArtifactFlowTests : IClassFixture<SyncApiFixture>
{
    private readonly SyncApiFixture _fixture;

    public ArtifactFlowTests(SyncApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Complete_AssembledBytesHashMismatch_Returns422ThenSucceedsAfterReupload()
    {
        var (_, client) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var capture = await SyncApiFixture.CreateCaptureAsync(client);

        // Declare goodContent's hash, but upload different bytes of the same length.
        var goodContent = SyncApiFixture.RandomBytes(100);
        var badContent = SyncApiFixture.RandomBytes(100);
        var declaredSha = SyncApiFixture.Sha256Hex(goodContent);
        var initiate = await SyncApiFixture.InitiateArtifactAsync(
            client, capture.CaptureId, goodContent);
        await SyncApiFixture.UploadPartsAsync(client, initiate.ArtifactId, badContent);

        var mismatch = await SyncApiFixture.CompleteArtifactAsync(
            client, initiate.ArtifactId, partCount: 2, totalByteLength: 100, sha256: declaredSha);
        mismatch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var mismatchError = await SyncApiFixture.ReadApiErrorAsync(mismatch);
        mismatchError.Code.Should().Be("artifact_hash_mismatch");
        mismatchError.Retryable.Should().BeTrue("the parts are kept so the upload can be corrected");

        // The rejected artifact is not downloadable.
        var download = await client.GetAsync($"/api/v1/artifacts/{initiate.ArtifactId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SyncApiFixture.ReadApiErrorAsync(download)).Code.Should().Be("artifact_not_complete");

        // Re-uploading the declared bytes and completing again succeeds.
        await SyncApiFixture.UploadPartsAsync(client, initiate.ArtifactId, goodContent);
        var retry = await SyncApiFixture.CompleteArtifactAsync(
            client, initiate.ArtifactId, partCount: 2, totalByteLength: 100, sha256: declaredSha);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var completed = await retry.Content.ReadFromJsonAsync<ArtifactCompleteResponse>(SyncApiFixture.Json);
        completed!.State.Should().Be(ArtifactState.Complete);
        completed.Sha256.Should().Be(declaredSha);

        var downloaded = await client.GetAsync($"/api/v1/artifacts/{initiate.ArtifactId}/download");
        downloaded.StatusCode.Should().Be(HttpStatusCode.OK);
        (await downloaded.Content.ReadAsByteArrayAsync()).Should().Equal(goodContent);
    }

    [Fact]
    public async Task Complete_DeclaredShaDiffersFromInitiate_Returns422HashMismatch()
    {
        var (_, client) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var capture = await SyncApiFixture.CreateCaptureAsync(client);

        var content = SyncApiFixture.RandomBytes(100);
        var initiate = await SyncApiFixture.InitiateArtifactAsync(client, capture.CaptureId, content);
        await SyncApiFixture.UploadPartsAsync(client, initiate.ArtifactId, content);

        // A well-formed but different sha (of other bytes) contradicts the initiate declaration.
        var wrongSha = SyncApiFixture.Sha256Hex(SyncApiFixture.RandomBytes(10));
        var response = await SyncApiFixture.CompleteArtifactAsync(
            client, initiate.ArtifactId, partCount: 2, totalByteLength: 100, sha256: wrongSha);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SyncApiFixture.ReadApiErrorAsync(response)).Code.Should().Be("artifact_hash_mismatch");
    }

    [Fact]
    public async Task Complete_WrongTotalByteLength_Returns422LengthMismatch()
    {
        var (_, client) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var capture = await SyncApiFixture.CreateCaptureAsync(client);

        var content = SyncApiFixture.RandomBytes(100);
        var initiate = await SyncApiFixture.InitiateArtifactAsync(client, capture.CaptureId, content);
        await SyncApiFixture.UploadPartsAsync(client, initiate.ArtifactId, content);

        var response = await SyncApiFixture.CompleteArtifactAsync(
            client, initiate.ArtifactId, partCount: 2, totalByteLength: 99,
            sha256: SyncApiFixture.Sha256Hex(content));
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SyncApiFixture.ReadApiErrorAsync(response)).Code.Should().Be("artifact_length_mismatch");
    }

    [Fact]
    public async Task Complete_AssembledShorterThanDeclared_Returns422LengthMismatch()
    {
        var (_, client) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var capture = await SyncApiFixture.CreateCaptureAsync(client);

        // Declare 100 bytes but upload only 96: the claim matches the
        // declaration, so the measured assembled length is what rejects it.
        var content = SyncApiFixture.RandomBytes(96);
        var initiate = await SyncApiFixture.InitiateArtifactAsync(
            client, capture.CaptureId, content, expectedByteLength: 100);
        await SyncApiFixture.UploadPartsAsync(client, initiate.ArtifactId, content);

        var response = await SyncApiFixture.CompleteArtifactAsync(
            client, initiate.ArtifactId, partCount: 2, totalByteLength: 100,
            sha256: SyncApiFixture.Sha256Hex(content));
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await SyncApiFixture.ReadApiErrorAsync(response);
        error.Code.Should().Be("artifact_length_mismatch");
        error.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task Complete_AlreadyCompleteWithSameSha_IsIdempotent()
    {
        var (_, client) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var capture = await SyncApiFixture.CreateCaptureAsync(client);

        var content = SyncApiFixture.RandomBytes(100);
        var sha = SyncApiFixture.Sha256Hex(content);
        var initiate = await SyncApiFixture.InitiateArtifactAsync(client, capture.CaptureId, content);
        await SyncApiFixture.UploadPartsAsync(client, initiate.ArtifactId, content);

        var first = await SyncApiFixture.CompleteArtifactAsync(
            client, initiate.ArtifactId, partCount: 2, totalByteLength: 100, sha256: sha);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await SyncApiFixture.CompleteArtifactAsync(
            client, initiate.ArtifactId, partCount: 2, totalByteLength: 100, sha256: sha);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = await replay.Content.ReadFromJsonAsync<ArtifactCompleteResponse>(SyncApiFixture.Json);
        replayed!.State.Should().Be(ArtifactState.Complete);
        replayed.ByteLength.Should().Be(content.Length);
        replayed.Sha256.Should().Be(sha);
    }

    [Fact]
    public async Task Download_BeforeCompletion_Returns409ApiError()
    {
        var (_, client) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var capture = await SyncApiFixture.CreateCaptureAsync(client);

        var content = SyncApiFixture.RandomBytes(100);
        var initiate = await SyncApiFixture.InitiateArtifactAsync(client, capture.CaptureId, content);

        // Only part 1 of 2 uploaded — never completed.
        var part = await SyncApiFixture.UploadPartAsync(
            client, initiate.ArtifactId, 1, content[..SyncApiFixture.PartSizeBytes]);
        part.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var download = await client.GetAsync($"/api/v1/artifacts/{initiate.ArtifactId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await SyncApiFixture.ReadApiErrorAsync(download);
        error.Code.Should().Be("artifact_not_complete");
    }

    [Fact]
    public async Task UploadPart_NonGuidArtifactId_Returns404WithoutTouchingDisk()
    {
        var (_, client) = await _fixture.RegisterClientAsync("ios", "iPhone");

        var response = await SyncApiFixture.UploadPartAsync(
            client, "..%2Fnot-a-guid", 1, new byte[] { 1, 2, 3 });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var plain = await SyncApiFixture.UploadPartAsync(client, "not-a-guid", 1, new byte[] { 1, 2, 3 });
        plain.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SyncApiFixture.ReadApiErrorAsync(plain)).Code.Should().Be("artifact_not_found");
    }

    [Fact]
    public async Task UploadPart_PartNumberOutsideExpectedRange_Returns400()
    {
        var (_, client) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var capture = await SyncApiFixture.CreateCaptureAsync(client);

        // 100 declared bytes at a 64-byte part size => parts 1..2 only.
        var content = SyncApiFixture.RandomBytes(100);
        var initiate = await SyncApiFixture.InitiateArtifactAsync(client, capture.CaptureId, content);
        initiate.ExpectedPartCount.Should().Be(2);

        var tooHigh = await SyncApiFixture.UploadPartAsync(
            client, initiate.ArtifactId, 3, content[..10]);
        tooHigh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SyncApiFixture.ReadApiErrorAsync(tooHigh)).Code.Should().Be("artifact_part_invalid");

        var zero = await SyncApiFixture.UploadPartAsync(client, initiate.ArtifactId, 0, content[..10]);
        zero.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SyncApiFixture.ReadApiErrorAsync(zero)).Code.Should().Be("artifact_part_invalid");
    }

    [Fact]
    public async Task UploadPart_LargerThanPartSize_Returns413()
    {
        var (_, client) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var capture = await SyncApiFixture.CreateCaptureAsync(client);

        var content = SyncApiFixture.RandomBytes(100);
        var initiate = await SyncApiFixture.InitiateArtifactAsync(client, capture.CaptureId, content);

        var oversized = SyncApiFixture.RandomBytes(SyncApiFixture.PartSizeBytes + 1);
        var response = await SyncApiFixture.UploadPartAsync(client, initiate.ArtifactId, 1, oversized);
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await SyncApiFixture.ReadApiErrorAsync(response)).Code.Should().Be("artifact_part_too_large");
    }
}
