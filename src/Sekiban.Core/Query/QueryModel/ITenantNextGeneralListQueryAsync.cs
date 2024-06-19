namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralListQueryAsync<TOutput> : INextGeneralQueryCommon<TOutput>, INextListQueryCommon<TOutput>, INextQueryAsyncGeneral,
    ITenantQueryCommon where TOutput : notnull;