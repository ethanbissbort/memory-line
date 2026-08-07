using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MemoryTimeline.SyncContracts;
using Xunit;

namespace MemoryTimeline.SyncApi.Tests;

/// <summary>
/// Refresh token rotation against a host with SyncApi:RefreshTokenGraceSeconds=0
/// (see <see cref="StrictRotationSyncApiFixture"/>): strict rotation, where the
/// pre-rotation token dies immediately and there is no recovery window.
/// </summary>
public class DeviceRefreshStrictRotationTests : IClassFixture<StrictRotationSyncApiFixture>
{
    private readonly StrictRotationSyncApiFixture _fixture;

    public DeviceRefreshStrictRotationTests(StrictRotationSyncApiFixture fixture) => _fixture = fixture;

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

        // Replaying the pre-rotation token is rejected — no grace window.
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
}
