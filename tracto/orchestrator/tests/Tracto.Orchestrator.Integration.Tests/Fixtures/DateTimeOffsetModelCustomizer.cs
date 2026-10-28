#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tracto.Orchestrator.Integration.Tests.Fixtures;

/// <summary>
/// Replaces the default EF Core model customizer in integration tests to add
/// UTC-ticks value converters for every <see cref="DateTimeOffset"/> /
/// <see cref="Nullable{DateTimeOffset}"/> property.
///
/// EF Core 9 + SQLite no longer translates <c>ORDER BY</c> on raw
/// <c>DateTimeOffset</c> columns (stored as ISO-8601 TEXT). Converting to
/// <c>long</c> (UTC ticks) lets SQLite sort by integer value, which is both
/// correct and fully supported.
///
/// This customizer is registered only in integration-test factories via
/// <c>DbContextOptionsBuilder.ReplaceService&lt;IModelCustomizer, DateTimeOffsetModelCustomizer&gt;()</c>
/// and has no effect on any production source file.
/// </summary>
public sealed class DateTimeOffsetModelCustomizer : ModelCustomizer
{
    // Shared converters — stateless, safe to reuse.
    private static readonly ValueConverter<DateTimeOffset, long> DtoToTicks =
        new(v => v.UtcTicks,
            v => new DateTimeOffset(v, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> DtoNullableToTicks =
        new(v => v.HasValue ? v.Value.UtcTicks : (long?)null,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : (DateTimeOffset?)null);

    public DateTimeOffsetModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <summary>
    /// Applies base model configuration first, then adds UTC-ticks converters
    /// to all un-converted DateTimeOffset properties so SQLite ORDER BY works.
    /// </summary>
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                // Skip properties that already have an explicit converter.
                if (property.GetValueConverter() is not null)
                    continue;

                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(DtoToTicks);
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(DtoNullableToTicks);
            }
        }
    }
}
