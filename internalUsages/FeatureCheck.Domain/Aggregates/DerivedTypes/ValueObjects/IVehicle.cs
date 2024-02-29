using System.Text.Json.Serialization;
namespace FeatureCheck.Domain.Aggregates.DerivedTypes.ValueObjects;

[JsonDerivedType(typeof(Bike), nameof(Bike))]
[JsonDerivedType(typeof(Car), nameof(Car))]
public interface IVehicle;
