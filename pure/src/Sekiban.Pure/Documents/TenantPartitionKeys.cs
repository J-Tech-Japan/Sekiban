namespace Sekiban.Pure.Documents;

public record TenantPartitionKeys(string TenantCode)
{
    public static TenantPartitionKeys Tenant(string tenantCode) => new(tenantCode);

    public PartitionKeys Generate(string group = PartitionKeys.DefaultAggregateGroupName) =>
        PartitionKeys.Generate(group, TenantCode);
    public PartitionKeys Existing(Guid aggregateId, string group = PartitionKeys.DefaultAggregateGroupName) =>
        PartitionKeys.Existing(aggregateId, group, TenantCode);
}
