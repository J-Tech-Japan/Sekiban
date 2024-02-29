using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.DerivedTypes.ValueObjects;

public record Car(
    [property: Required]
    string Color,
    string Name) : IVehicle
{
    public static Car Empty => new(string.Empty, string.Empty);
}
