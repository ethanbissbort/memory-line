using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MemoryTimeline.SyncContracts;
using Xunit;

namespace MemoryTimeline.SyncApi.Tests;

/// <summary>
/// Device lifecycle flows against the real host: pairing-gated registration,
/// bearer-token enforcement on every authenticated route, refresh token
/// rotation (old token dies, new access token works), and immediate
/// revocation by a peer device.
/// </summary>
public class DeviceFlowTests : IClassFixture<SyncApiFixture>
{
    private readonly SyncApiFixture _fixture;

    public DeviceFlowTests(SyncApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Register_ValidPairingCode_Returns201WithCredentialsAndListsDevice()
    {
        using var client = _fixture.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/devices/register",
            new DeviceRegisterRequest
            {
                PairingCode = SyncApiFixture.PairingCode,
                Platform = "ios",
                DisplayName = "iPhone 15",
                AppVersion = "1.0.0",
            },
            SyncApiFixture.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var registered = await response.Content.ReadFromJsonAsync<DeviceRegisterResponse>(SyncApiFixture.Json);
        registered!.DeviceId.Should().NotBeNullOrEmpty();
        Guid.TryParse(registered.DeviceId, out _).Should().BeTrue("device IDs are GUIDs");
        registered.OwnerId.Should().NotBeNullOrEmpty();
        registered.AccessToken.Should().NotBeNullOrEmpty();
        registered.RefreshToken.Should().NotBeNullOrEmpty();
        registered.AccessTokenExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);

        using var authed = _fixture.CreateClient(registered.AccessToken);
        var devices = await authed.GetFromJsonAsync<List<DeviceInfoResponse>>(
            "/api/v1/devices", SyncApiFixture.Json);
        devices!.Should().Contain(d =>
            d.DeviceId == registered.DeviceId && d.Platform == "ios" && d.RevokedAtUtc == null);
    }

    [Fact]
    public async Task Register_WrongPairingCode_Returns401WithApiError()
    {
        using var client = _fixture.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/devices/register",
            new DeviceRegisterRequest
            {
                PairingCode = "wrong-pairing-code",
                Platform = "ios",
                DisplayName = "Intruder",
            },
            SyncApiFixture.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var error = await SyncApiFixture.ReadApiErrorAsync(response);
        error.Code.Should().Be("invalid_pairing_code");
        error.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticatedRoutes_WithoutBearerToken_Return401()
    {
        using var client = _fixture.CreateClient();

        var pull = await client.GetAsync("/api/v1/sync/pull?cursor=0");
        pull.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var captureCreate = await client.PostAsJsonAsync(
            "/api/v1/captures", SyncApiFixture.NewCaptureRequest(), SyncApiFixture.Json);
        captureCreate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var download = await client.GetAsync($"/api/v1/artifacts/{Guid.NewGuid():D}/download");
        download.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var partUpload = await SyncApiFixture.UploadPartAsync(
            client, Guid.NewGuid().ToString("D"), 1, new byte[] { 1, 2, 3 });
        partUpload.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var list = await client.GetAsync("/api/v1/devices");
        list.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var error = await SyncApiFixture.ReadApiErrorAsync(list);
        error.Code.Should().Be("unauthorized");
    }

    [Fact]
    public async Task AuthenticatedRoutes_WithGarbageBearerToken_Return401()
    {
        using var client = _fixture.CreateClient("not-a-valid-jwt");
        var response = await client.GetAsync("/api/v1/devices");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var error = await SyncApiFixture.ReadApiErrorAsync(response);
        error.Code.Should().Be("unauthorized");
    }

    [Fact]
    public async Task Refresh_RotatesRefreshToken_OldTokenDiesAndNewAccessTokenWorks()
    {
        var device = await _fixture.RegisterDeviceAsync("windows", "Desktop");
        using var client = _fixture.CreateClient();

        // First refresh with the original token succeeds and rotates.
        var first = await client.PostAsJsonAsync(
            $"/api/v1/devices/{device.DeviceId}/refresh",
            new TokenRefreshRequest { RefreshToken = device.RefreshToken },
            SyncApiFixture.Json);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await first.Content.ReadFromJsonAsync<TokenRefreshResponse>(SyncApiFixture.Json);
        rotated!.AccessToken.Should().NotBeNullOrEmpty();
        rotated.RefreshToken.Should().NotBeNullOrEmpty();
        rotated.RefreshToken.Should().NotBe(device.RefreshToken, "refresh rotates the token");

        // Replaying the pre-rotation token is rejected.
        var replay = await client.PostAsJsonAsync(
            $"/api/v1/devices/{device.DeviceId}/refresh",
            new TokenRefreshRequest { RefreshToken = device.RefreshToken },
            SyncApiFixture.Json);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var error = await SyncApiFixture.ReadApiErrorAsync(replay);
        error.Code.Should().Be("refresh_token_invalid");

        // The rotated access token authenticates.
        using var authed = _fixture.CreateClient(rotated.AccessToken);
        var list = await authed.GetAsync("/api/v1/devices");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        // And the rotated refresh token is the live credential.
        var second = await client.PostAsJsonAsync(
            $"/api/v1/devices/{device.DeviceId}/refresh",
            new TokenRefreshRequest { RefreshToken = rotated.RefreshToken },
            SyncApiFixture.Json);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revoke_ByPeerDevice_RejectsAccessTokenAndRefreshImmediately()
    {
        var (deviceA, clientA) = await _fixture.RegisterClientAsync("ios", "iPhone");
        var (deviceB, clientB) = await _fixture.RegisterClientAsync("windows", "Desktop");
        using (clientA)
        using (clientB)
        {
            // Windows (B) revokes the iPhone (A).
            var revoke = await clientB.DeleteAsync($"/api/v1/devices/{deviceA.DeviceId}");
            revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // A's still-unexpired access token is rejected by the device filter.
            var rejected = await clientA.GetAsync("/api/v1/devices");
            rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var accessError = await SyncApiFixture.ReadApiErrorAsync(rejected);
            accessError.Code.Should().Be("device_revoked");

            // A's refresh token is rejected too.
            using var anonymous = _fixture.CreateClient();
            var refresh = await anonymous.PostAsJsonAsync(
                $"/api/v1/devices/{deviceA.DeviceId}/refresh",
                new TokenRefreshRequest { RefreshToken = deviceA.RefreshToken },
                SyncApiFixture.Json);
            refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var refreshError = await SyncApiFixture.ReadApiErrorAsync(refresh);
            refreshError.Code.Should().Be("device_revoked");

            // The revoking device still works and sees the revocation recorded.
            var devices = await clientB.GetFromJsonAsync<List<DeviceInfoResponse>>(
                "/api/v1/devices", SyncApiFixture.Json);
            devices!.Should().Contain(d => d.DeviceId == deviceA.DeviceId && d.RevokedAtUtc != null);
        }
    }
}
