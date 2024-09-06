namespace Sekiban.Core.Query.QueryModel;

public interface INextQueryCommon<TQuery, TOutput> : INextQueryCommonOutput<TOutput> where TOutput : notnull
    where TQuery : INextQueryCommon<TQuery, TOutput>;
public interface INextQueryCommonOutput<TOutput> : INextQueryCommon, INextQueryGeneral<TOutput> where TOutput : notnull;
public interface INextQueryCommon;
