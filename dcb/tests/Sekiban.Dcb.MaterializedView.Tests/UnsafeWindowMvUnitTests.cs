using Dcb.Domain.WithoutResult.MaterializedViews;
using Dcb.Domain.WithoutResult.Weather;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

// ============================================================================
// Unsafe Window Materialized View v1 — unit tests for the public contract.
//
// Full transactional / reordering / promotion behavior is verified in the
// Postgres integration tests (Sekiban.Dcb.MaterializedView.Postgres.Tests);
// these tests focus on the pure fold / projection-key / tag-mapping contract
// so regressions surface without spinning up a database.
// ============================================================================

public class UnsafeWindowMvUnitTests
{
    private readonly WeatherForecastUnsafeWindowMvV1 _projector = new();

    [Fact]
    public void Schema_Declares_All_Business_Columns()
    {
        Assert.Equal(
            new[] { "forecast_id", "location", "forecast_date", "temperature_c", "summary" },
            _projector.Schema.Columns.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void TagsForProjectionKey_Returns_Weather_Tag_For_Guid_Keys()
    {
        var forecastId = Guid.NewGuid();
        var tags = _projector.TagsForProjectionKey(forecastId.ToString());
        var tag = Assert.Single(tags);
        Assert.Equal(new WeatherForecastTag(forecastId), tag);
    }

    [Fact]
    public void TagsForProjectionKey_Returns_Empty_For_Non_Guid_Keys()
    {
        Assert.Empty(_projector.TagsForProjectionKey("not-a-guid"));
    }

    [Fact]
    public void Apply_NoChange_For_Unrelated_Event()
    {
        var ev = CreateEvent(new UnrelatedPayload());
        var outcome = _projector.Apply(null, ev);
        Assert.IsType<UnsafeWindowMvApplyOutcome.NoChange>(outcome);
    }

    [Fact]
    public void Apply_Created_Produces_Upsert_With_Projection_Key()
    {
        var forecastId = Guid.NewGuid();
        var created = new WeatherForecastCreated(forecastId, "Tokyo", new DateOnly(2026, 4, 17), 15, "Cloudy");
        var outcome = _projector.Apply(null, CreateEvent(created));

        var upsert = Assert.IsType<UnsafeWindowMvApplyOutcome.Upsert>(outcome);
        Assert.Equal(forecastId.ToString(), upsert.ProjectionKey);
        var row = Assert.IsType<WeatherForecastUnsafeRow>(upsert.Row);
        Assert.Equal("Tokyo", row.Location);
        Assert.Equal(15, row.TemperatureC);
    }

    [Fact]
    public void Apply_Updated_Overwrites_Existing_Row()
    {
        var forecastId = Guid.NewGuid();
        var current = new WeatherForecastUnsafeRow
        {
            ForecastId = forecastId,
            Location = "Old",
            ForecastDate = new DateOnly(2026, 4, 17),
            TemperatureC = 10,
            Summary = "Old"
        };
        var updated = new WeatherForecastUpdated(forecastId, "New", new DateOnly(2026, 4, 17), 20, "Sunny");
        var outcome = _projector.Apply(current, CreateEvent(updated));

        var upsert = Assert.IsType<UnsafeWindowMvApplyOutcome.Upsert>(outcome);
        var row = Assert.IsType<WeatherForecastUnsafeRow>(upsert.Row);
        Assert.Equal("New", row.Location);
        Assert.Equal(20, row.TemperatureC);
    }

    [Fact]
    public void Apply_LocationNameChanged_Preserves_Other_Columns()
    {
        var forecastId = Guid.NewGuid();
        var current = new WeatherForecastUnsafeRow
        {
            ForecastId = forecastId,
            Location = "Old",
            ForecastDate = new DateOnly(2026, 4, 17),
            TemperatureC = 17,
            Summary = "Mild"
        };

        var renamed = new LocationNameChanged(forecastId, "Renamed", "Old");
        var outcome = _projector.Apply(current, CreateEvent(renamed));

        var upsert = Assert.IsType<UnsafeWindowMvApplyOutcome.Upsert>(outcome);
        var row = Assert.IsType<WeatherForecastUnsafeRow>(upsert.Row);
        Assert.Equal("Renamed", row.Location);
        Assert.Equal(17, row.TemperatureC);
        Assert.Equal("Mild", row.Summary);
    }

    [Fact]
    public void Apply_Deleted_Produces_Delete_Outcome()
    {
        var forecastId = Guid.NewGuid();
        var outcome = _projector.Apply(null, CreateEvent(new WeatherForecastDeleted(forecastId)));
        var deletion = Assert.IsType<UnsafeWindowMvApplyOutcome.Delete>(outcome);
        Assert.Equal(forecastId.ToString(), deletion.ProjectionKey);
    }

    [Fact]
    public void Apply_Delete_Then_Create_Resurrects_Row()
    {
        // Simulates recreate-after-delete scenario: Apply a delete first (row
        // becomes deleted), then apply a new Create for the same id. Create
        // always builds a fresh row, so the row is resurrected.
        var forecastId = Guid.NewGuid();
        var deleted = _projector.Apply(null, CreateEvent(new WeatherForecastDeleted(forecastId)));
        Assert.IsType<UnsafeWindowMvApplyOutcome.Delete>(deleted);

        var created = _projector.Apply(
            null,
            CreateEvent(new WeatherForecastCreated(forecastId, "Resurrected", new DateOnly(2026, 4, 17), 25, null)));
        var upsert = Assert.IsType<UnsafeWindowMvApplyOutcome.Upsert>(created);
        var row = Assert.IsType<WeatherForecastUnsafeRow>(upsert.Row);
        Assert.Equal("Resurrected", row.Location);
    }

    [Fact]
    public void Schema_Column_Getter_Extracts_Expected_Value()
    {
        var row = new WeatherForecastUnsafeRow
        {
            ForecastId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Location = "Osaka",
            ForecastDate = new DateOnly(2026, 4, 17),
            TemperatureC = 22,
            Summary = "Humid"
        };

        var values = _projector.Schema.Columns.ToDictionary(c => c.Name, c => c.Getter(row));
        Assert.Equal(row.ForecastId, values["forecast_id"]);
        Assert.Equal("Osaka", values["location"]);
        Assert.Equal(22, values["temperature_c"]);
        Assert.Equal("Humid", values["summary"]);
    }

    private static Event CreateEvent(IEventPayload payload)
    {
        var id = Guid.NewGuid();
        return new Event(
            payload,
            Common.SortableUniqueId.GenerateNew(),
            payload.GetType().Name,
            id,
            new EventMetadata(id.ToString(), id.ToString(), "unit-test"),
            new List<string>());
    }

    private sealed record UnrelatedPayload : IEventPayload;
}
