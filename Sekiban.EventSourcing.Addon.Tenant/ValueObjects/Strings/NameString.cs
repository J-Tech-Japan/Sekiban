using Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;
using Sekiban.EventSourcing.Addon.Tenant.Extensions;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
namespace Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Strings;

public record NameString : StringValueBase
{
    public const int MaxLength = 20;
    public NameString(string value) : base(value) { }

    protected override void Validate()
    {
        base.Validate();

        if (Value.Length > MaxLength)
        {
            throw new OverflowValidationError(GetType().GetDisplayName() ?? ValueObjectAttributes.EmailStringDisplayName, MaxLength);
        }
    }
    public static implicit operator string(NameString vo) =>
        vo.Value;
    public static implicit operator NameString(string v) =>
        new(v);
}
