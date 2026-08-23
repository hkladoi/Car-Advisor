using VietnamCarPlatform.Api.Features.Catalog;
using VietnamCarPlatform.Infrastructure.Catalog;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class CatalogFilterTests
{
    [Fact]
    public void NormalizeRemovesVietnameseDiacriticsAndPreservesTokens()
    {
        Assert.Equal("o to dien vf 6", SearchNormalizer.Normalize(" Ô tô điện VF 6 "));
        Assert.Equal("dong co", SearchNormalizer.Normalize("Động cơ"));
    }

    [Fact]
    public void FeatureModeAndRequiresEveryCanonicalFeatureOnSameTrim()
    {
        var filter = CreateFilter("ACC,AEB", "and");

        Assert.True(filter.Matches(Car("ACC", "AEB", "CAMERA_360")));
        Assert.False(filter.Matches(Car("ACC", "CAMERA_360")));
    }

    [Fact]
    public void FeatureModeOrRequiresAtLeastOneCanonicalFeature()
    {
        var filter = CreateFilter("ACC,AEB", "or");

        Assert.True(filter.Matches(Car("AEB")));
        Assert.False(filter.Matches(Car("CAMERA_360")));
    }

    [Fact]
    public void MultiTokenSearchReturnsHonestPartialCandidate()
    {
        var filter = CreateFilter(search: "tucson hybrid");
        var tucson = Car();
        tucson.ModelName = "Tucson";
        tucson.SearchText = "hyundai tucson tucson 1 6 t gdi petrol diesel ice";

        Assert.True(filter.Matches(tucson));
        Assert.True(filter.SearchScore(tucson) >= 300);
        Assert.DoesNotContain("hybrid", tucson.SearchText, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidRangeAndModeAreRejectedBeforeDatabaseQuery()
    {
        var request = new CatalogRequest
        {
            FeatureMode = "xor",
            MsrpMin = 900_000_000,
            MsrpMax = 500_000_000,
        };

        var valid = CatalogFilter.TryCreate(request, out var filter, out var errors);

        Assert.False(valid);
        Assert.Null(filter);
        Assert.Contains(nameof(request.FeatureMode), errors.Keys);
        Assert.Contains(nameof(request.MsrpMax), errors.Keys);
    }

    private static CatalogFilter CreateFilter(
        string? features = null,
        string featureMode = "and",
        string? search = null)
    {
        var request = new CatalogRequest
        {
            Search = search,
            Features = features,
            FeatureMode = featureMode,
        };
        Assert.True(CatalogFilter.TryCreate(request, out var filter, out var errors), string.Join(';', errors.Keys));
        return filter!;
    }

    private static CurrentSearchableTrim Car(params string[] featureCodes) => new()
    {
        TrimId = Guid.NewGuid(),
        BrandSlug = "test-brand",
        ModelSlug = "test-model",
        ModelName = "Test Model",
        BodyType = "Suv",
        Segment = "C",
        PowertrainType = "Ice",
        SearchText = "test brand test model",
        FeatureCodes = featureCodes,
        ColorCodes = [],
    };
}
