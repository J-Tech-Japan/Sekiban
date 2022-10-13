using Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Bases;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.ValueObjects;

public record TenantCodeString : SingleValueObjectNoValidationClassBase<string>
{
    public TenantCodeString(string value) : base(value) { }

    public static implicit operator string(TenantCodeString vo)
    {
        return vo.Value;
    }
    public static implicit operator TenantCodeString(string v)
    {
        return new(v);
    }
}
