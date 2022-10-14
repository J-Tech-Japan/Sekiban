namespace Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Bases;

public abstract record class SingleValueObjectNoValidationClassBase<T> : ISingleValueObject<T>
{
    public SingleValueObjectNoValidationClassBase(T value)
    {
        Value = value;
    }
    public T Value { get; init; }
}
