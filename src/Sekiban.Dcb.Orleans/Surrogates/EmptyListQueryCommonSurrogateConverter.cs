using Orleans;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Orleans.Surrogates;

/// <summary>
/// Orleans surrogate converter for EmptyListQueryCommon
/// </summary>
[RegisterConverter]
public sealed class EmptyListQueryCommonSurrogateConverter : IConverter<EmptyListQueryCommon, EmptyListQueryCommonSurrogate>
{
    public EmptyListQueryCommon ConvertFromSurrogate(in EmptyListQueryCommonSurrogate surrogate) =>
        new EmptyListQueryCommon();

    public EmptyListQueryCommonSurrogate ConvertToSurrogate(in EmptyListQueryCommon value) =>
        new EmptyListQueryCommonSurrogate();
}