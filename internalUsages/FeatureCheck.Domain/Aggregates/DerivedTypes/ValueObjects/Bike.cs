namespace FeatureCheck.Domain.Aggregates.DerivedTypes.ValueObjects;

public record Bike(string Color, string Name, string Brand) : IVehicle
{
    public static Bike Empty => new(string.Empty, string.Empty, string.Empty);
}
