using Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;
using Sekiban.EventSourcing.Addon.Tenant.Extensions;
using Sekiban.EventSourcing.Addon.Tenant.Globalization;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
using System.Text.RegularExpressions;
namespace Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Strings;

[ResourceDisplayName(typeof(ValueObjectAttributes), nameof(ValueObjectAttributes.ZipCodeDisplayName))]
public record class ZipCodeString : AsciiString
{
    public const int MaxLength = 8;
    public const string RegularExpressionPattern = @"^[0-9-]+$";

    public ZipCodeString(string value) : base(value) { }

    protected override void Validate()
    {
        base.Validate();

        if (!Regex.IsMatch(Value, RegularExpressionPattern))
        {
            throw new InvalidCharacterValidationError(
                GetType().GetDisplayName() ?? ValueObjectAttributes.ZipCodeDisplayName,
                ValueObjectAttributes.ZipCodeStringAcceptableCharacterTypes);
        }

        if (Value.Length > MaxLength)
        {
            throw new OverflowValidationError(GetType().GetDisplayName() ?? ValueObjectAttributes.ZipCodeDisplayName, MaxLength);
        }
    }

    public static implicit operator string(ZipCodeString vo) =>
        vo.Value;
    public static implicit operator ZipCodeString(string v) =>
        new(v);
}
