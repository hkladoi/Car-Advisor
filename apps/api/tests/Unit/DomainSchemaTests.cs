using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Accounts;
using VietnamCarPlatform.Domain.Admin;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Partners;
using VietnamCarPlatform.Domain.Sources;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class DomainSchemaTests
{
    [Fact]
    public void TrimIdentityIsUniqueWithinModelYear()
    {
        using var db = CreateContext();
        var trim = db.Model.FindEntityType(typeof(Trim));

        var uniqueIdentity = Assert.Single(trim!.GetIndexes().Where(index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Trim.ModelYearId), nameof(Trim.NormalizedKey)])));

        Assert.True(uniqueIdentity.IsUnique);
    }

    [Fact]
    public void EffectivePeriodIsHalfOpenAndRejectsInvalidBounds()
    {
        var from = new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var period = new EffectivePeriod(from, to);

        Assert.True(period.Contains(from));
        Assert.False(period.Contains(to));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectivePeriod(from, from));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectivePeriod(from, from.AddTicks(-1)));
    }

    [Fact]
    public void UnknownIsNotTheSameAsOfficialFalse()
    {
        var unknown = new TrimFeature { Status = FactStatus.Unknown, BooleanValue = null };
        var officialFalse = new TrimFeature { Status = FactStatus.Official, BooleanValue = false };

        Assert.True(FactSemantics.AllowsValue(unknown.Status, unknown.BooleanValue.HasValue));
        Assert.True(FactSemantics.AllowsValue(officialFalse.Status, officialFalse.BooleanValue.HasValue));
        Assert.False(FactSemantics.AllowsValue(FactStatus.Unknown, hasValue: true));
        Assert.False(FactSemantics.AllowsValue(FactStatus.Official, hasValue: false));
        Assert.NotEqual(unknown.BooleanValue, officialFalse.BooleanValue);
    }

    [Fact]
    public void DatabaseModelCarriesEffectiveDateAndUnknownCheckConstraints()
    {
        using var db = CreateContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;
        var priceChecks = designModel.FindEntityType(typeof(Price))!.GetCheckConstraints();
        var featureChecks = designModel.FindEntityType(typeof(TrimFeature))!.GetCheckConstraints();

        Assert.Contains(priceChecks, check => check.Name == "ck_prices_effective_period");
        Assert.Contains(featureChecks, check => check.Name == "ck_trim_features_value_semantics");
    }

    [Fact]
    public void AdminSessionsLocksAndAuditReasonsHaveDatabaseGuards()
    {
        using var db = CreateContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;

        Assert.Contains(designModel.FindEntityType(typeof(AdminSession))!.GetCheckConstraints(), check => check.Name == "ck_admin_sessions_expiry");
        Assert.Contains(designModel.FindEntityType(typeof(FieldLock))!.GetCheckConstraints(), check => check.Name == "ck_field_locks_reason");
        Assert.Contains(designModel.FindEntityType(typeof(VietnamCarPlatform.Domain.Sources.AuditEvent))!.GetCheckConstraints(), check => check.Name == "ck_audit_events_reason");
    }

    [Fact]
    public void OptInAccountsOwnSessionsComparisonsAndWatchlistWithPrivacyGuards()
    {
        using var db = CreateContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;

        Assert.Contains(designModel.FindEntityType(typeof(UserAccount))!.GetCheckConstraints(),
            check => check.Name == "ck_user_accounts_consent");
        Assert.Contains(designModel.FindEntityType(typeof(UserSession))!.GetCheckConstraints(),
            check => check.Name == "ck_user_sessions_expiry");
        Assert.Contains(designModel.FindEntityType(typeof(SavedComparison))!.GetCheckConstraints(),
            check => check.Name == "ck_saved_comparisons_trim_ids");
        Assert.Contains(designModel.FindEntityType(typeof(WatchlistEntry))!.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(WatchlistEntry.UserAccountId), nameof(WatchlistEntry.TrimId)]));
        Assert.All(designModel.FindEntityType(typeof(UserSession))!.GetForeignKeys(),
            key => Assert.Equal(DeleteBehavior.Cascade, key.DeleteBehavior));
    }

    [Fact]
    public void ReviewedPublicationHasRollbackGuardAndOneVersionPerChange()
    {
        using var db = CreateContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;
        var publication = designModel.FindEntityType(typeof(PublicationVersion))!;

        Assert.Contains(publication.GetCheckConstraints(), check => check.Name == "ck_publication_versions_rollback");
        Assert.Contains(publication.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(PublicationVersion.DataChangeId)]));
        Assert.Contains(publication.GetForeignKeys(), key => key.Properties.Single().Name == nameof(PublicationVersion.BeforeSourceFactId));
        Assert.Contains(publication.GetForeignKeys(), key => key.Properties.Single().Name == nameof(PublicationVersion.SourceFactId));
    }

    [Fact]
    public void MonitoringRunAndAlertLifecycleAreGuarded()
    {
        using var db = CreateContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;

        Assert.Contains(designModel.FindEntityType(typeof(IngestionJobRun))!.GetCheckConstraints(),
            check => check.Name == "ck_ingestion_job_runs_lifecycle");
        Assert.Contains(designModel.FindEntityType(typeof(MonitoringAlert))!.GetCheckConstraints(),
            check => check.Name == "ck_monitoring_alerts_lifecycle");
        Assert.Contains(designModel.FindEntityType(typeof(MonitoringAlert))!.GetIndexes(),
            index => index.IsUnique && index.Properties.Single().Name == nameof(MonitoringAlert.Fingerprint));
    }

    [Fact]
    public void ChargingLocationsCannotBecomeUnreviewedTariffAuthority()
    {
        using var db = CreateContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;
        var station = designModel.FindEntityType(typeof(ChargingStation))!;

        Assert.Contains(station.GetCheckConstraints(), check => check.Name == "ck_charging_stations_reference_coverage");
        Assert.Contains(station.GetCheckConstraints(), check => check.Name == "ck_charging_stations_provider_mapping");
        Assert.Contains(station.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ChargingStation.ExternalSource), nameof(ChargingStation.ExternalId)]));
        Assert.DoesNotContain(station.GetProperties(), property =>
            property.Name.Contains("Tariff", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Price", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Cost", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RealWorldCohortsRequireSampleMethodologyIdentityAndProvenance()
    {
        using var db = CreateContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;
        var aggregate = designModel.FindEntityType(typeof(RealWorldConsumptionAggregate))!;

        Assert.Contains(aggregate.GetCheckConstraints(), check => check.Name == "ck_real_world_consumption_sample");
        Assert.Contains(aggregate.GetCheckConstraints(), check => check.Name == "ck_real_world_consumption_provenance");
        Assert.Contains(aggregate.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(RealWorldConsumptionAggregate.DatasetVersion),
                nameof(RealWorldConsumptionAggregate.VehicleRegistrationYear),
                nameof(RealWorldConsumptionAggregate.NormalizedManufacturer),
                nameof(RealWorldConsumptionAggregate.FuelType),
            ]));
    }

    [Fact]
    public void PublishedDataEventsHaveRetryLifecycleAndProjectionQueueIndexes()
    {
        using var db = CreateContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;
        var dataEvent = designModel.FindEntityType(typeof(PublishedDataEvent))!;

        Assert.Contains(dataEvent.GetCheckConstraints(), check => check.Name == "ck_published_data_events_attempts");
        Assert.Contains(dataEvent.GetCheckConstraints(), check => check.Name == "ck_published_data_events_lifecycle");
        Assert.Contains(dataEvent.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual([
                nameof(PublishedDataEvent.Status),
                nameof(PublishedDataEvent.AvailableAt),
                nameof(PublishedDataEvent.OccurredAt),
            ]));
    }

    [Fact]
    public void PartnerApiKeysHaveHashedReadOnlyLifecycleAndPlanGuards()
    {
        using var db = CreateContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;
        var key = designModel.FindEntityType(typeof(PartnerApiKey))!;
        var plan = designModel.FindEntityType(typeof(PartnerApiUsagePlan))!;

        Assert.Contains(key.GetCheckConstraints(), check => check.Name == "ck_partner_api_keys_hash");
        Assert.Contains(key.GetCheckConstraints(), check => check.Name == "ck_partner_api_keys_scope");
        Assert.Contains(key.GetCheckConstraints(), check => check.Name == "ck_partner_api_keys_revocation");
        Assert.Contains(key.GetIndexes(), index => index.IsUnique
            && index.Properties.Single().Name == nameof(PartnerApiKey.KeyPrefix));
        Assert.Contains(plan.GetCheckConstraints(), check => check.Name == "ck_partner_api_usage_plans_limits");
        Assert.Contains(plan.GetIndexes(), index => index.IsUnique
            && index.Properties.Single().Name == nameof(PartnerApiUsagePlan.Code));
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model_only;Password=model_only")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }
}
