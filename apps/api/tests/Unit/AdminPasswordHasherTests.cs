using VietnamCarPlatform.Api.Features.Admin;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class AdminPasswordHasherTests
{
    [Fact]
    public void HashUsesSaltAndVerifiesInConstantTimeBoundary()
    {
        const string password = "correct horse battery staple";
        var first = AdminPasswordHasher.Hash(password);
        var second = AdminPasswordHasher.Hash(password);

        Assert.NotEqual(first, second);
        Assert.True(AdminPasswordHasher.Verify(password, first));
        Assert.False(AdminPasswordHasher.Verify("wrong password", first));
        Assert.False(AdminPasswordHasher.Verify(password, "not-a-valid-hash"));
    }
}
