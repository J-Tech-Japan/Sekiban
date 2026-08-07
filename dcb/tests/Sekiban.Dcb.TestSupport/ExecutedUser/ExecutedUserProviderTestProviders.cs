using Sekiban.Dcb.Actors;
namespace Sekiban.Dcb.TestSupport.ExecutedUser;

/// <summary>Executed-user provider that always returns the same value.</summary>
public sealed class ConstantExecutedUserProvider : IExecutedUserProvider
{
    private readonly string? _value;
    public ConstantExecutedUserProvider(string? value) => _value = value;
    public string GetExecutedUser() => _value ?? string.Empty;
}

/// <summary>Executed-user provider that returns null, used to verify fallback behavior.</summary>
public sealed class NullExecutedUserProvider : IExecutedUserProvider
{
    public string GetExecutedUser() => null!;
}

/// <summary>Executed-user provider that returns a configured sequence of values and counts calls.</summary>
public sealed class SequenceExecutedUserProvider : IExecutedUserProvider
{
    private readonly IReadOnlyList<string> _values;
    private int _index;
    public SequenceExecutedUserProvider(params string[] values) => _values = values;
    public int CallCount => _index;
    public string GetExecutedUser()
    {
        var value = _index < _values.Count ? _values[_index] : $"extra-{_index}";
        _index++;
        return value;
    }
}

/// <summary>Executed-user provider that throws, used to prove the serialized path does not call it.</summary>
public sealed class ThrowingExecutedUserProvider : IExecutedUserProvider
{
    public string GetExecutedUser() =>
        throw new InvalidOperationException("Provider must not be consulted on the serialized path.");
}
