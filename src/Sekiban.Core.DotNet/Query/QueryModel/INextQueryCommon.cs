namespace Sekiban.Core.Query.QueryModel;

public interface INextQueryCommon<TQuery, TOutput> : INextQueryCommonOutput<TOutput> where TOutput : notnull
    where TQuery : INextQueryCommon<TQuery, TOutput>, IEquatable<TQuery>;
public interface INextQueryCommon;
