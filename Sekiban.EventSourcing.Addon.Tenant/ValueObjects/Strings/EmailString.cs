using Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;
using Sekiban.EventSourcing.Addon.Tenant.Extensions;
using Sekiban.EventSourcing.Addon.Tenant.Globalization;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
using System.Text.RegularExpressions;
namespace Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Strings;

[ResourceDisplayName(typeof(ValueObjectAttributes), nameof(ValueObjectAttributes.EmailStringDisplayName))]
public record class EmailString : AsciiString
{
    public const int MaxLength = 200;
    public const string RegularExpressionPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    public EmailString(string value) : base(value) { }

    protected override void Validate()
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

    public static implicit operator string(EmailString vo) =>
        vo.Value;
    public static implicit operator EmailString(string v) =>
        new(v);
}
