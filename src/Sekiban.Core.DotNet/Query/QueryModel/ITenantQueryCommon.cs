namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Tenant Query Parameter Interface.
///     Query developers does not need to implement this interface directly.
/// </summary>
public interface ITenantQueryCommon
{
    public string GetTenantId();
}
