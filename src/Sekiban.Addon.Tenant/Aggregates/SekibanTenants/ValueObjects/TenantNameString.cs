using Sekiban.Addon.Tenant.ValueObjects.Bases;
namespace Sekiban.Addon.Tenant.Aggregates.SekibanTenants.ValueObjects;

public record TenantNameString : SingleValueObjectNoValidationClassBase<string>
{
    public TenantNameString(string value) : base(value) { }
    public static implicit operator string(TenantNameString vo)
    {
        return vo.Value;
    }
    public static implicit operator TenantNameString(string v)
    {
        return new(v);
    }
}