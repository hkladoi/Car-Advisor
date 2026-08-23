using VietnamCarPlatform.Api.Middleware;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public void ResolveCorrelationIdPreservesSafeClientValue()
    {
        const string requested = "web-req_2026.08.22";

        var actual = CorrelationIdMiddleware.ResolveCorrelationId(requested);

        Assert.Equal(requested, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains spaces")]
    [InlineData("bad/header")]
    public void ResolveCorrelationIdReplacesUnsafeValue(string requested)
    {
        var actual = CorrelationIdMiddleware.ResolveCorrelationId(requested);

        Assert.NotEqual(requested, actual);
        Assert.Equal(32, actual.Length);
    }
}
