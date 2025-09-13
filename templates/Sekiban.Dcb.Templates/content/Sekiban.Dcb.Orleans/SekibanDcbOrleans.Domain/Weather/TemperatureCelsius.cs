namespace Dcb.Domain.Weather;

public readonly record struct TemperatureCelsius(double Value)
{
    public int ToInt() => (int)System.Math.Round(Value);

    public static implicit operator TemperatureCelsius(int value) => new(value);
    public static implicit operator int(TemperatureCelsius value) => value.ToInt();
}

