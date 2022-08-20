using Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;
using Sekiban.EventSourcing.Addon.Tenant.Extensions;
using Sekiban.EventSourcing.Addon.Tenant.Globalization;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
using System.Text.RegularExpressions;
namespace Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Strings;

[ResourceDisplayName(typeof(ValueObjectAttributes), nameof(ValueObjectAttributes.TelDisplayName))]
public record class TelString : AsciiString
{
    public const int MaxLength = 21;
    public const string RegularExpressionPattern = @"^[0-9-\+]+$";

    public TelString(string value) : base(value) { }

    protected override void Validate()
    {
        base.Validate();

        if (!Regex.IsMatch(Value, RegularExpressionPattern))
        {
            throw new InvalidCharacterValidationError(
                GetType().GetDisplayName() ?? ValueObjectAttributes.TelDisplayName,
                ValueObjectAttributes.TelStringAcceptableCharacterTypes);
        }

        if (Value.Length > MaxLength)
        {
            throw new OverflowValidationError(GetType().GetDisplayName() ?? ValueObjectAttributes.TelDisplayName, MaxLength);
        }
    }

    public static implicit operator string(TelString vo) =>
        vo.Value;
    public static implicit operator TelString(string v) =>
        new(v);
}
public static class TelStringExtension
{
    public static TelString ToTelString(this string v) =>
        new(v);
}
