using Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;
using Sekiban.EventSourcing.Addon.Tenant.Extensions;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
using Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Strings;
using System.Text.RegularExpressions;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.ValueObjects;

public record TenantCodeString : AsciiString
{
    private const int MaxLength = 20;
    public const string RegularExpressionPattern = @"^[a-zA-Z0-9\-_]+$";

    public TenantCodeString(string value) : base(value) { }

    protected void Validate(string value)
    {
        base.Validate();

        if (!Regex.IsMatch(Value, RegularExpressionPattern, RegexOptions.IgnoreCase))
        {
            throw new InvalidFormatValidationError(GetType().GetDisplayName() ?? ValueObjectAttributes.EmailStringDisplayName);
        }

        if (Value.Length > MaxLength)
        {
            throw new OverflowValidationError(GetType().GetDisplayName() ?? ValueObjectAttributes.EmailStringDisplayName, MaxLength);
        }
    }
    public static implicit operator string(TenantCodeString vo) =>
        vo.Value;
    public static implicit operator TenantCodeString(string v) =>
        new(v);
}
