using VietnamCarPlatform.Api.Features.Admin;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class AdminManualImportValidatorTests
{
    private const string Header = "brand_name,brand_slug,model_name,model_slug,generation_code,model_year,trim_name,trim_slug,source_url,body_type,segment,market_status,powertrain,price_type,msrp_amount,seats,length_mm,width_mm,height_mm,wheelbase_mm";

    [Fact]
    public void ValidCsvProducesReviewableRecordWithoutErrors()
    {
        var csv = $"{Header}\nTest,test,Car,car,G1,2026,Car Base,car-base,https://example.com/car,Suv,B,Active,Bev,Msrp,500000000,5,4300,1800,1600,2700";

        var result = AdminManualImportValidator.Parse("vehicle.csv", csv);

        Assert.Single(result.Records);
        Assert.DoesNotContain(result.Issues, issue => issue.Severity == "Error");
    }

    [Fact]
    public void DuplicateIdentityAndImpossibleDimensionAreReported()
    {
        var row = "Test,test,Car,car,G1,2026,Car Base,car-base,https://example.com/car,Suv,B,Active,Bev,Msrp,500000000,5,45000,1800,1600,2700";

        var result = AdminManualImportValidator.Parse("vehicle.csv", $"{Header}\n{row}\n{row}");

        Assert.Contains(result.Issues, issue => issue.Code == "IMPOSSIBLE_VALUE" && issue.Field == "length_mm");
        Assert.Contains(result.Issues, issue => issue.Code == "DUPLICATE_TRIM_IDENTITY");
    }

    [Fact]
    public void CsvQuotedCommaIsParsedAsOneField()
    {
        var csv = $"{Header}\n\"Test, Vietnam\",test,Car,car,G1,2026,Car Base,car-base,https://example.com/car,Suv,B,Active,Bev,Msrp,500000000,5,4300,1800,1600,2700";

        var result = AdminManualImportValidator.Parse("vehicle.csv", csv);

        Assert.Equal("Test, Vietnam", result.Records[0]["brand_name"]);
    }
}
