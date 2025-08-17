using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Weather;

public class WeatherForecastProjector : ITagProjector<WeatherForecastProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(WeatherForecastProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as WeatherForecastState ?? new WeatherForecastState();

        return ev.Payload switch
        {
            WeatherForecastCreated created => state with
            {
                ForecastId = created.ForecastId,
                Location = created.Location,
                Date = created.Date,
                TemperatureC = created.TemperatureC,
                Summary = created.Summary,
                IsDeleted = false
            },

            WeatherForecastUpdated updated => state with
            {
                Location = updated.Location,
                Date = updated.Date,
                TemperatureC = updated.TemperatureC,
                Summary = updated.Summary
            },

            WeatherForecastDeleted => state with
            {
                IsDeleted = true
            },

            _ => state
        };
    }
}
