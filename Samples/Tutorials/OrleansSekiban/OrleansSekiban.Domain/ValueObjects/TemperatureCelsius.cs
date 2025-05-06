using System.ComponentModel.DataAnnotations;

namespace OrleansSekiban.Domain.ValueObjects;

[GenerateSerializer]
public record TemperatureCelsius([property:Range(-273.15, 1000000.0, ErrorMessage = "Temperature cannot be below absolute zero (-273.15°C)")] double Value)
{
    public double GetFahrenheit() => Value * 9 / 5 + 32;
}
