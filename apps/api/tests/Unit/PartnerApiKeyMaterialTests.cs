using VietnamCarPlatform.Api.Features.PartnerApi;
using VietnamCarPlatform.Domain.Partners;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class PartnerApiKeyMaterialTests
{
    [Fact]
    public void GeneratedKeyIsHighEntropyPrefixedAndOnlyHashIsPersistable()
    {
        var first = PartnerApiKeyMaterial.Generate();
        var second = PartnerApiKeyMaterial.Generate();

        Assert.StartsWith("vcp_v1_", first.Token, StringComparison.Ordinal);
        Assert.Equal(61, first.Token.Length);
        Assert.Equal(17, first.Prefix.Length);
        Assert.Equal(64, first.Hash.Length);
        Assert.DoesNotContain(first.Token, first.Hash, StringComparison.Ordinal);
        Assert.NotEqual(first.Token, second.Token);
        Assert.True(PartnerApiKeyMaterial.TryGetPrefix(first.Token, out var parsedPrefix));
        Assert.Equal(first.Prefix, parsedPrefix);
        Assert.True(PartnerApiKeyMaterial.FixedTimeEquals(first.Hash, first.Token));
        Assert.False(PartnerApiKeyMaterial.FixedTimeEquals(first.Hash, second.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("vcp_v1_too-short")]
    [InlineData("vcp_v1_abcdefghij.invalid+")]
    public void MalformedKeyNeverProducesALookupPrefix(string? token)
    {
        Assert.False(PartnerApiKeyMaterial.TryGetPrefix(token, out var prefix));
        Assert.Empty(prefix);
    }

    [Fact]
    public void RevokedOrExpiredKeyIsNeverActive()
    {
        var now = new DateTimeOffset(2026, 8, 24, 4, 0, 0, TimeSpan.Zero);
        var active = new PartnerApiKey { ExpiresAt = now.AddMinutes(1) };
        var expired = new PartnerApiKey { ExpiresAt = now };
        var revoked = new PartnerApiKey { RevokedAt = now.AddMinutes(-1) };

        Assert.True(active.IsActiveAt(now));
        Assert.False(expired.IsActiveAt(now));
        Assert.False(revoked.IsActiveAt(now));
    }
}
