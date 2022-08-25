using Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Bases;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.ValueObjects;

public record TenantNameString : SingleValueObjectNoValidationClassBase<string>
{
    public TenantNameString(string value) : base(value) { }
    public static implicit operator string(TenantNameString vo) =>
        vo.Value;
    public static implicit operator TenantNameString(string v) =>
        new(v);
}
