using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.TenantUsers;

public record TenantUser(string Name, string Email) : ITenantAggregatePayload<TenantUser>
{
    public static TenantUser CreateInitialPayload(TenantUser? _) => new(string.Empty, string.Empty);
}
