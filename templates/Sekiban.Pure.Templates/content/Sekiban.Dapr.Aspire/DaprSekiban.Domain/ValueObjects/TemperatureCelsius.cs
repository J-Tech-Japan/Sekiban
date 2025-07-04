using System.ComponentModel.DataAnnotations;
using Orleans;

namespace DaprSekiban.Domain.ValueObjects;

[GenerateSerializer]
public record TemperatureCelsius([property:Id(0)][property:Range(-273.15, 1000000.0, ErrorMessage = "Temperature cannot be below absolute zero (-273.15°C)")] double Value)
{
    public double GetFahrenheit() => Value * 9 / 5 + 32;
}