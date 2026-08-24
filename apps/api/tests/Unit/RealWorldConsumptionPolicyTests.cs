using VietnamCarPlatform.Domain.Catalog;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class RealWorldConsumptionPolicyTests
{
    [Fact]
    public void LatestCohortsSelectLatestRegistrationYearAndLargestSamplePerFuel()
    {
        var selected = RealWorldConsumptionSelectionPolicy.LatestCohorts([
            Row(2022, "TOYOTA", "PETROL/ELECTRIC", 5_694),
            Row(2023, "TOYOTA", "PETROL/ELECTRIC", 1_702),
            Row(2023, "TOYOTA MOTOR CORPORATION", "PETROL/ELECTRIC", 8_000),
            Row(2023, "TOYOTA", "PETROL", 2_000),
        ]);

        Assert.Equal(2, selected.Count);
        Assert.All(selected, value => Assert.Equal(2023, value.VehicleRegistrationYear));
        Assert.Equal("TOYOTA MOTOR CORPORATION", selected.Single(value => value.FuelType == "PETROL/ELECTRIC").Manufacturer);
        Assert.Equal(8_000, selected.Single(value => value.FuelType == "PETROL/ELECTRIC").SampleSize);
    }

    [Theory]
    [InlineData("Ice", null, "E10Ron95III", true, "PETROL")]
    [InlineData("Phev", null, "E10Ron95III", true, "PETROL/ELECTRIC")]
    [InlineData("Ice", "Diesel", null, true, "DIESEL")]
    [InlineData("Bev", null, null, false, null)]
    [InlineData("Unknown", null, null, true, null)]
    public void FuelCompatibilityUsesOnlyExplicitPowertrainFacts(
        string powertrain,
        string? fuelType,
        string? recommendedFuel,
        bool hasLiquidFuel,
        string? cohortFuel)
    {
        var result = RealWorldConsumptionSelectionPolicy.ResolveFuel(powertrain, fuelType, recommendedFuel);

        Assert.Equal(hasLiquidFuel, result.HasLiquidFuel);
        Assert.Equal(cohortFuel, result.CohortFuelType);
    }

    [Fact]
    public void ExplicitPetrolTrimDoesNotReceiveDieselOrPhevCohorts()
    {
        var selected = RealWorldConsumptionSelectionPolicy.LatestCohorts([
            Row(2023, "TOYOTA", "DIESEL", 636),
            Row(2023, "TOYOTA", "PETROL", 37_146),
            Row(2023, "TOYOTA", "PETROL/ELECTRIC", 1_702),
        ], new RealWorldFuelSelection(true, "PETROL"));

        var row = Assert.Single(selected);
        Assert.Equal("PETROL", row.FuelType);
    }

    [Fact]
    public void BevGetsNoLiquidFuelCohort()
    {
        Assert.Empty(RealWorldConsumptionSelectionPolicy.LatestCohorts([
            Row(2023, "GEELY", "PETROL", 200),
        ], new RealWorldFuelSelection(false, null)));
    }

    [Fact]
    public void EmptyCandidateSetReturnsNoReferenceRatherThanInventingOne()
    {
        Assert.Empty(RealWorldConsumptionSelectionPolicy.LatestCohorts([]));
    }

    private static RealWorldConsumptionAggregate Row(int year, string manufacturer, string fuel, int sample) => new()
    {
        DatasetReportingYear = 2024,
        VehicleRegistrationYear = year,
        DatasetVersion = "fixture",
        Manufacturer = manufacturer,
        NormalizedManufacturer = manufacturer,
        FuelType = fuel,
        SampleSize = sample,
        RealWorldFuelLitresPer100Km = 5,
        OfficialWltpFuelLitresPer100Km = 4,
        SourceFactId = Guid.NewGuid(),
    };
}
