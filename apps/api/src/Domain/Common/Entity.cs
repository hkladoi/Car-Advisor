namespace VietnamCarPlatform.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public abstract class EffectiveDatedEntity : Entity
{
    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveTo { get; set; }

    public bool IsEffectiveAt(DateTimeOffset instant) =>
        instant >= EffectiveFrom && (EffectiveTo is null || instant < EffectiveTo.Value);
}

public abstract class SourcedEntity : Entity
{
    public Guid? SourceFactId { get; set; }

    public string? ManualOverrideReason { get; set; }
}

public abstract class EffectiveSourcedEntity : EffectiveDatedEntity
{
    public Guid? SourceFactId { get; set; }

    public string? ManualOverrideReason { get; set; }
}

public readonly record struct EffectivePeriod
{
    public EffectivePeriod(DateTimeOffset from, DateTimeOffset? to)
    {
        if (to is not null && from >= to.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(to), "EffectiveTo must be later than EffectiveFrom.");
        }

        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }

    public DateTimeOffset? To { get; }

    public bool Contains(DateTimeOffset instant) => instant >= From && (To is null || instant < To.Value);
}
