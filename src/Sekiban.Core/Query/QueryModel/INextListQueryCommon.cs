namespace Sekiban.Core.Query.QueryModel;

public interface INextListQueryCommon<TOutput> : INextListQueryCommon, INextQueryGeneral<TOutput>
    where TOutput : notnull;
public interface INextListQueryCommon;
