using Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;
using Sekiban.EventSourcing.Addon.Tenant.Extensions;
using Sekiban.EventSourcing.Addon.Tenant.Globalization;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
using System.Text;
namespace Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Strings;

[ResourceDisplayName(typeof(ValueObjectAttributes), nameof(ValueObjectAttributes.AsciiStringDisplayName))]
public record class AsciiString : StringValueBase
{
    public AsciiString(string value) : base(value) { }

    protected override void Validate()
    {
        base.Validate();

        // 「Shift_JISのバイト数＝文字数」なら英数字のみとみなす。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // Shift-JISを扱うための呼び出し
        var enc = Encoding.GetEncoding("Shift_JIS");
        if (enc.GetByteCount(Value) != Value.Length)
        {
            throw new InvalidCharacterValidationError(
                GetType().GetDisplayName() ?? ValueObjectAttributes.AsciiStringDisplayName,
                ValueObjectAttributes.AsciiStringAcceptableCharacterTypes);
        }
    }
    public static implicit operator string(AsciiString vo) =>
        vo.Value;
    public static implicit operator AsciiString(string v) =>
        new(v);
}
