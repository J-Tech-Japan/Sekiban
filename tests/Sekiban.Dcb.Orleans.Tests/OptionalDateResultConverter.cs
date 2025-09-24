using ResultBoxes;
namespace Sekiban.Dcb.Orleans.Tests;

[RegisterConverter]
public sealed class OptionalDateResultConverter : IConverter<OptionalDateResult, OptionalDateResultSurrogate>
{
    public OptionalDateResult ConvertFromSurrogate(in OptionalDateResultSurrogate surrogate) =>
        new OptionalDateResult(
            surrogate.HasValue
                ? OptionalValue<DateOnly>.FromValue(surrogate.Value)
                : OptionalValue<DateOnly>.Empty);

    public OptionalDateResultSurrogate ConvertToSurrogate(in OptionalDateResult value) =>
        value.Date.HasValue
            ? new OptionalDateResultSurrogate(true, value.Date.GetValue())
            : new OptionalDateResultSurrogate(false, default);
}