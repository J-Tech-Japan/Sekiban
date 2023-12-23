using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
namespace Sekiban.Core.Command;

/// <summary>
///     Validate Root Partition Key
/// </summary>
public class CommandRootPartitionValidationAttribute : ValidationAttribute
{
    private const string RootPartitionKeyRegexPattern = "^[a-z0-9-_]{1,36}$";
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not ICommandCommon command)
        {
            return new ValidationResult("Property is not ICommandCommon");
        }
        var rootPartitionKey = command.GetRootPartitionKey();

        if (string.IsNullOrWhiteSpace(rootPartitionKey))
        {
            return new ValidationResult("Root Partition Key only allow a-z, 0-9, -, _ and length 1-36");
        }

        return Regex.IsMatch(rootPartitionKey, RootPartitionKeyRegexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(250))
            ? ValidationResult.Success
            : new ValidationResult("Root Partition Key only allow a-z, 0-9, -, _ and length 1-36");

    }

    public static bool IsValidRootPartitionKey(string rootPartitionKey)
    {
        if (string.IsNullOrWhiteSpace(rootPartitionKey))
        {
            return false;
        }

        if (Regex.IsMatch(rootPartitionKey, RootPartitionKeyRegexPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)))
        {
            return true;
        }

        return false;
    }
}
