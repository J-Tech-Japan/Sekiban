using Orleans;

namespace SharedDomain.ValueObjects;

[GenerateSerializer]
public record TemperatureCelsius
{
    public int Value { get; init; }

    public TemperatureCelsius(int value)
    {
        if (value < -273)
        {
            throw new ArgumentException("Temperature cannot be below absolute zero.");
        }
        Value = value;
    }

    public static implicit operator TemperatureCelsius(int value) => new(value);
    public static implicit operator int(TemperatureCelsius temperature) => temperature.Value;

    public int ToFahrenheit() => 32 + (int)(Value / 0.5556);
}