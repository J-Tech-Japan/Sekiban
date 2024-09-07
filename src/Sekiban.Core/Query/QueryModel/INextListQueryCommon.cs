namespace Sekiban.Core.Query.QueryModel;

public interface INextListQueryCommon<TQuery, TOutput> : INextListQueryCommonOutput<TOutput> where TOutput : notnull
    where TQuery : INextListQueryCommon<TQuery, TOutput>;
public interface INextListQueryCommonOutput<TOutput> : INextListQueryCommon, INextQueryGeneral<TOutput>
    where TOutput : notnull;
public interface INextListQueryCommon;
