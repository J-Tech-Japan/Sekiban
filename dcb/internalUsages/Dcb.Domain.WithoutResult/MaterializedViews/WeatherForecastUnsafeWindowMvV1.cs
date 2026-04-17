using Dcb.Domain.WithoutResult.Weather;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.Tags;

namespace Dcb.Domain.WithoutResult.MaterializedViews;

/// <summary>
///     Reference Unsafe Window MV for WeatherForecast (issue #1028).
///
///     One projection key per forecast id. The row carries the business
///     columns (location / forecast_date / temperature_c / summary) declared
///     through <see cref="Schema" />; framework metadata columns
///     (_projection_key / _last_sortable_unique_id / _is_deleted / …) are
///     managed by the runtime.
/// </summary>
public sealed class WeatherForecastUnsafeWindowMvV1 : IUnsafeWindowMvProjector<WeatherForecastUnsafeRow>
{
    public string ViewName => "WeatherForecastUnsafeWindow";
    public int ViewVersion => 1;

    // Short safe window for the sample so promotions are observable via curl
    // without waiting minutes. Production projectors should set this to match
    // the MultiProjection dynamic-safe-window heuristic.
    public TimeSpan SafeWindow => TimeSpan.FromSeconds(2);

    public UnsafeWindowMvSchema Schema { get; } = new(
        new UnsafeWindowMvColumn(
            "forecast_id",
            "UUID NOT NULL",
            row => ((WeatherForecastUnsafeRow)row).ForecastId),
        new UnsafeWindowMvColumn(
            "location",
            "TEXT NOT NULL",
            row => ((WeatherForecastUnsafeRow)row).Location),
        new UnsafeWindowMvColumn(
            "forecast_date",
            "DATE NOT NULL",
            row => ((WeatherForecastUnsafeRow)row).ForecastDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
        new UnsafeWindowMvColumn(
            "temperature_c",
            "INT NOT NULL",
            row => ((WeatherForecastUnsafeRow)row).TemperatureC),
        new UnsafeWindowMvColumn(
            "summary",
            "TEXT NULL",
            row => (object?)((WeatherForecastUnsafeRow)row).Summary));

    public string? GetProjectionKey(Event ev) => ev.Payload switch
    {
        WeatherForecastCreated c => ProjectionKeyFor(c.ForecastId),
        WeatherForecastUpdated u => ProjectionKeyFor(u.ForecastId),
        LocationNameChanged l => ProjectionKeyFor(l.ForecastId),
        WeatherForecastDeleted d => ProjectionKeyFor(d.ForecastId),
        _ => null
    };

    public UnsafeWindowMvApplyOutcome Apply(WeatherForecastUnsafeRow? current, Event ev) =>
        ev.Payload switch
        {
            WeatherForecastCreated created => new UnsafeWindowMvApplyOutcome.Upsert(
                ProjectionKeyFor(created.ForecastId),
                new WeatherForecastUnsafeRow
                {
                    ForecastId = created.ForecastId,
                    Location = created.Location,
                    ForecastDate = created.Date,
                    TemperatureC = created.TemperatureC,
                    Summary = created.Summary
                }),

            WeatherForecastUpdated updated => new UnsafeWindowMvApplyOutcome.Upsert(
                ProjectionKeyFor(updated.ForecastId),
                new WeatherForecastUnsafeRow
                {
                    ForecastId = updated.ForecastId,
                    Location = updated.Location,
                    ForecastDate = updated.Date,
                    TemperatureC = updated.TemperatureC,
                    Summary = updated.Summary
                }),

            LocationNameChanged changed when current is not null => new UnsafeWindowMvApplyOutcome.Upsert(
                ProjectionKeyFor(changed.ForecastId),
                current with { Location = changed.NewLocationName }),

            WeatherForecastDeleted deleted => new UnsafeWindowMvApplyOutcome.Delete(
                ProjectionKeyFor(deleted.ForecastId)),

            _ => new UnsafeWindowMvApplyOutcome.NoChange()
        };

    public IReadOnlyList<ITag> TagsForProjectionKey(string projectionKey) =>
        Guid.TryParse(projectionKey, out var forecastId)
            ? [new WeatherForecastTag(forecastId)]
            : [];

    private static string ProjectionKeyFor(Guid forecastId) => forecastId.ToString();
}

public sealed record WeatherForecastUnsafeRow
{
    public Guid ForecastId { get; init; }
    public string Location { get; init; } = string.Empty;
    public DateOnly ForecastDate { get; init; }
    public int TemperatureC { get; init; }
    public string? Summary { get; init; }
}
