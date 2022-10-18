using Sekiban.Addon.Tenant.ValueObjects.Bases;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Addon.Tenant.Validations;

public class ValueObjectConvertibleAttribute<TValueObject> : ValidationAttribute where TValueObject : class, ISingleValueObjectBase
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        var valueType = typeof(TValueObject).GetProperty("Value")?.PropertyType;

        if (valueType is null) { return new ValidationResult("値の形式が正しくありません。"); }

        var valueObject = Convert.ChangeType(value, valueType);
        try
        {
            dynamic? valueObjectInstance = Activator.CreateInstance(typeof(TValueObject), valueObject) as TValueObject;
            return valueObjectInstance?.Value?.GetType() == valueType ? ValidationResult.Success : new ValidationResult("値の形式が正しくありません。");
        }
        catch (Exception ex)
        {
            return new ValidationResult(ex.InnerException?.Message ?? ex.Message);
        }
    }
}
