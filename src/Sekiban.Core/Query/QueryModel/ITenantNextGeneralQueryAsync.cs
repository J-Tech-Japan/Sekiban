namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextGeneralQueryAsync<TOutput> : INextGeneralQueryCommon<TOutput>, INextQueryCommon<TOutput>, INextQueryAsyncGeneral,
    ITenantQueryCommon where TOutput : notnull;