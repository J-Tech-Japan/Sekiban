namespace Sekiban.Core.Query.QueryModel;

public interface INextListQueryCommonOutput<TOutput> : INextListQueryCommon, INextQueryGeneral<TOutput>
    where TOutput : notnull;
