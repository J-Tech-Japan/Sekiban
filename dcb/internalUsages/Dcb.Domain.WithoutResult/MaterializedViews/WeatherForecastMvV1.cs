using Dcb.Domain.WithoutResult.Weather;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;

namespace Dcb.Domain.WithoutResult.MaterializedViews;

public sealed class WeatherForecastMvV1 : IMaterializedViewProjector
{
    public string ViewName => "WeatherForecast";
    public int ViewVersion => 1;

    public MvTable Forecasts { get; private set; } = default!;

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        Forecasts = ctx.RegisterTable("forecasts");

        await ctx.ExecuteAsync(
            $"""
             CREATE TABLE IF NOT EXISTS {Forecasts.PhysicalName} (
                 forecast_id UUID PRIMARY KEY,
                 location TEXT NOT NULL,
                 forecast_date DATE NOT NULL,
                 temperature_c INT NOT NULL,
                 summary TEXT NULL,
                 is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                 _last_sortable_unique_id TEXT NOT NULL,
                 _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
             );
             """,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ctx.ExecuteAsync(
            $"""
             CREATE INDEX IF NOT EXISTS idx_{Forecasts.PhysicalName}_forecast_date
             ON {Forecasts.PhysicalName} (forecast_date DESC);
             """,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MvSqlStatement>>(
            ev.Payload switch
            {
                WeatherForecastCreated created => [UpsertForecast(created.ForecastId, created.Location, created.Date, created.TemperatureC, created.Summary, false, ctx.CurrentSortableUniqueId)],
                WeatherForecastUpdated updated => [UpsertForecast(updated.ForecastId, updated.Location, updated.Date, updated.TemperatureC, updated.Summary, false, ctx.CurrentSortableUniqueId)],
                LocationNameChanged changed => [UpdateLocation(changed.ForecastId, changed.NewLocationName, ctx.CurrentSortableUniqueId)],
                WeatherForecastDeleted deleted => [MarkDeleted(deleted.ForecastId, ctx.CurrentSortableUniqueId)],
                _ => []
            });

    private MvSqlStatement UpsertForecast(
        Guid forecastId,
        string location,
        DateOnly forecastDate,
        int temperatureC,
        string? summary,
        bool isDeleted,
        string sortableUniqueId) =>
        new(
            $"""
             INSERT INTO {Forecasts.PhysicalName}
                 (forecast_id, location, forecast_date, temperature_c, summary, is_deleted, _last_sortable_unique_id, _last_applied_at)
             VALUES
                 (@ForecastId, @Location, @ForecastDate, @TemperatureC, @Summary, @IsDeleted, @SortableUniqueId, NOW())
             ON CONFLICT (forecast_id) DO UPDATE SET
                 location = EXCLUDED.location,
                 forecast_date = EXCLUDED.forecast_date,
                 temperature_c = EXCLUDED.temperature_c,
                 summary = EXCLUDED.summary,
                 is_deleted = EXCLUDED.is_deleted,
                 _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                 _last_applied_at = EXCLUDED._last_applied_at
             WHERE {Forecasts.PhysicalName}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
             """,
            new
            {
                ForecastId = forecastId,
                Location = location,
                ForecastDate = forecastDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                TemperatureC = temperatureC,
                Summary = summary,
                IsDeleted = isDeleted,
                SortableUniqueId = sortableUniqueId
            });

    private MvSqlStatement UpdateLocation(Guid forecastId, string location, string sortableUniqueId) =>
        new(
            $"""
             UPDATE {Forecasts.PhysicalName}
             SET location = @Location,
                 _last_sortable_unique_id = @SortableUniqueId,
                 _last_applied_at = NOW()
             WHERE forecast_id = @ForecastId
               AND _last_sortable_unique_id < @SortableUniqueId;
             """,
            new
            {
                ForecastId = forecastId,
                Location = location,
                SortableUniqueId = sortableUniqueId
            });

    private MvSqlStatement MarkDeleted(Guid forecastId, string sortableUniqueId) =>
        new(
            $"""
             UPDATE {Forecasts.PhysicalName}
             SET is_deleted = TRUE,
                 _last_sortable_unique_id = @SortableUniqueId,
                 _last_applied_at = NOW()
             WHERE forecast_id = @ForecastId
               AND _last_sortable_unique_id < @SortableUniqueId;
             """,
            new
            {
                ForecastId = forecastId,
                SortableUniqueId = sortableUniqueId
            });
}

public sealed class WeatherForecastMvRow
{
    [MvColumn("forecast_id")]
    public Guid ForecastId { get; set; }

    [MvColumn("location")]
    public string Location { get; set; } = string.Empty;

    [MvColumn("forecast_date")]
    public DateTimeOffset ForecastDate { get; set; }

    [MvColumn("temperature_c")]
    public int TemperatureC { get; set; }

    [MvColumn("summary")]
    public string? Summary { get; set; }

    [MvColumn("is_deleted")]
    public bool IsDeleted { get; set; }

    [MvColumn("_last_sortable_unique_id")]
    public string LastSortableUniqueId { get; set; } = string.Empty;

    [MvColumn("_last_applied_at")]
    public DateTimeOffset LastAppliedAt { get; set; }
}
