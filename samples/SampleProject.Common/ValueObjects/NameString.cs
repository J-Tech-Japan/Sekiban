namespace ESSampleProjectLib.ValueObjects;

public record NameString : IValueObject<string>
{

    public NameString(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidValueException("名前を空白にすることはできません。");
        }

        Value = name;
    }
    public string Value { get; }

    public static implicit operator string(NameString vo) => vo.Value;
    public static implicit operator NameString(string v) => new NameString(v);
}
