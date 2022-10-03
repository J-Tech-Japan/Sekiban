using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.Addon.Tenant.Validations;

[AttributeUsage(AttributeTargets.Property)]
public class GuidRequiredAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value == null)
        {
            return false;
        }
        if (value is Guid guidValue)
        {
            if (guidValue == Guid.Empty)
            {
                return false;
            }
            return true;
        }
        return false;
    }
    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        if (IsValid(value))
        {
            return ValidationResult.Success!;
        }
        return new ValidationResult($"{validationContext.MemberName} is empty.", new[] { validationContext?.MemberName ?? string.Empty });
    }
}
