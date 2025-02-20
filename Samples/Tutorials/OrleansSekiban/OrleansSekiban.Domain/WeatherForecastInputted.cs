using Sekiban.Pure.Events;

namespace OrleansSekiban.Domain;

public record WeatherForecastInputted(
    string Location,
    DateTime Date,
    int TemperatureC,
    string Summary
) : IEventPayload;