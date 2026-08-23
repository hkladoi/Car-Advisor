using VietnamCarPlatform.Api.Features.Accounts;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class AccountPasswordHasherTests
{
    [Fact]
    public void HashUsesIndependentSaltAndRejectsMalformedValues()
    {
        const string password = "member-password-2026";
        var first = AccountPasswordHasher.Hash(password);
        var second = AccountPasswordHasher.Hash(password);

        Assert.NotEqual(first, second);
        Assert.True(AccountPasswordHasher.Verify(password, first));
        Assert.False(AccountPasswordHasher.Verify("wrong-password-2026", first));
        Assert.False(AccountPasswordHasher.Verify(password, "not-a-hash"));
    }
}
