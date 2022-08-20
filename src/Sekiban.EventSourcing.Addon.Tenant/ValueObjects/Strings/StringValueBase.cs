using Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;
using Sekiban.EventSourcing.Addon.Tenant.Extensions;
using Sekiban.EventSourcing.Addon.Tenant.Globalization;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
using Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Bases;
namespace Sekiban.EventSourcing.Addon.Tenant.ValueObjects.Strings;

[ResourceDisplayName(typeof(ValueObjectAttributes), nameof(ValueObjectAttributes.StringValueBaseDisplayName))]
public abstract record class StringValueBase : SingleValueObjectClassBase<string>
{
    protected StringValueBase(string value) : base(value?.Trim() ?? string.Empty) { }

    protected override void Validate()
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new RequiredFieldValidationError(GetType().GetDisplayName() ?? ValueObjectAttributes.StringValueBaseDisplayName);
        }
    }
}
