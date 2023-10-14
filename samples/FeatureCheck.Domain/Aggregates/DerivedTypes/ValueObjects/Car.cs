namespace FeatureCheck.Domain.Aggregates.DerivedTypes.ValueObjects;

public record Car(string Color, string Name) : IVehicle
{
    public static Car Empty => new(string.Empty, string.Empty);
}
