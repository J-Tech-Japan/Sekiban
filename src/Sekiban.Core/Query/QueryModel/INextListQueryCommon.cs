namespace Sekiban.Core.Query.QueryModel;

public interface INextListQueryCommon<TQuery, TOutput> : INextListQueryCommonOutput<TOutput> where TOutput : notnull
    where TQuery : INextListQueryCommon<TQuery, TOutput>, IEquatable<TQuery>;
public interface INextListQueryCommon;
