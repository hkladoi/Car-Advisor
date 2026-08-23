using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Admin;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Sources;
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

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only;Username=model_only;Password=model_only")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }
}
