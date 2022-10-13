namespace ESSampleProjectLib.ValueObjects;

public interface IValueObject<T>
{
    T Value { get; }
}
