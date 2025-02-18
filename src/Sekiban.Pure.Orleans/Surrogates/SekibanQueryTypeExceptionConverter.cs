using Sekiban.Pure.Exceptions;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class SekibanQueryTypeExceptionConverter : IConverter<SekibanQueryTypeException, SekibanQueryTypeExceptionSurrogate>
{
    public SekibanQueryTypeException ConvertFromSurrogate(in SekibanQueryTypeExceptionSurrogate surrogate) =>
        new(surrogate.Message);

    public SekibanQueryTypeExceptionSurrogate ConvertToSurrogate(in SekibanQueryTypeException value) =>
        new(value.Message);
}
