namespace Sekiban.Addon.Tenant.ValueObjects.Bases;

public interface ISingleValueObjectBase
{
}
public interface ISingleValueObject<T> : ISingleValueObjectBase
{
    T Value { get; }
}
