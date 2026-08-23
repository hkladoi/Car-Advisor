using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using VietnamCarPlatform.Api.Features.Charging;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class ChargingIntegrationTests
{
    [Theory]
    [InlineData(null, "Open Charge Map did not provide a data-quality level; not provider verified.")]
    [InlineData(3, "Open Charge Map community data quality 3/5; medium reference confidence and not provider verified.")]
    public void ConfidenceAlwaysDisclosesCommunityReferenceSemantics(int? level, string expected)
    {
        Assert.Equal(expected, ChargingService.ConfidenceBasis(level));
        Assert.Contains("not provider verified", ChargingService.ConfidenceBasis(level), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("address\u0001")]
    public void GoongAddressValidationRejectsUnsafeOrUnboundedInput(string address)
    {
        var error = Assert.Throws<ChargingIntegrationException>(() =>
            GoongGeocodingClient.ValidateAddress(address));

        Assert.Equal("GEOCODE_ADDRESS_INVALID", error.Code);
    }

    [Fact]
    public async Task GoongGeocodeUsesServerKeyAndHashedResultCache()
    {
        var requests = new List<Uri>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"status":"OK","results":[{"formatted_address":"91 Trung Kính, Hà Nội","place_id":"place-1","geometry":{"location":{"lat":21.013762524,"lng":105.798267363}}}]}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var client = new GoongGeocodingClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://rsapi.goong.io/") },
            new GoongOptions { ApiKey = "server-only-key", CacheSeconds = 60 },
            cache,
            TimeProvider.System);

        var first = await client.ForwardAsync(" 91  Trung Kính, Hà Nội ", CancellationToken.None);
        var second = await client.ForwardAsync("91 Trung Kính, Hà Nội", CancellationToken.None);

        Assert.False(first.Cached);
        Assert.True(second.Cached);
        Assert.Single(requests);
        Assert.Contains("api_key=server-only-key", requests[0].Query, StringComparison.Ordinal);
        Assert.Equal(21.013762524m, first.Results[0].Latitude);
        Assert.Equal("91 Trung Kính, Hà Nội", first.Results[0].FormattedAddress);
    }

    [Fact]
    public async Task MissingGoongKeyDegradesWithoutAProviderRequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call provider"));
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var client = new GoongGeocodingClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://rsapi.goong.io/") },
            new GoongOptions(),
            cache,
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<ChargingIntegrationException>(() =>
            client.ForwardAsync("Hà Nội", CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);
        Assert.Equal("GOONG_NOT_CONFIGURED", error.Code);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
