using System.Collections;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.Validations;

public static class ValidationExtensions
{
    public static IEnumerable<(int, ValidationResult)> TryValidateEnumerable(this IEnumerable collection)
    {
        var validationResults = new List<ValidationResult>();
        var list = collection.Cast<object>().ToList();
        foreach (var (item, index) in list.Select((item, index) => (item, index)))
        {
            if (item is null)
            {
                continue;
            }
            validationResults.Clear();
            validationResults.AddRange(item.TryValidateProperties());
            foreach (var validationResult in validationResults)
            {
                yield return (index, validationResult);
            }
        }
    }

    public static IEnumerable<(int, ValidationResult)> TryValidateEnumerable<T>(this IEnumerable<T> collection)
    {
        var validationResults = new List<ValidationResult>();
        var list = collection.ToList();
        foreach (var (item, index) in list.Select((item, index) => (item, index)))
        {
            if (item is null)
            {
                continue;
            }
            validationResults.Clear();
            validationResults.AddRange(item.TryValidateProperties());
            foreach (var validationResult in validationResults)
            {
                yield return (index, validationResult);
            }
        }
    }

    public static IEnumerable<ValidationResult> TryValidateProperties(this object targetClass, string baseKeyPath = "")
    {
        var validationResults = new List<ValidationResult>();

        // オブジェクトがコレクションの場合
        if (targetClass is IEnumerable collection)
        {
            foreach (var (index, validationResult) in collection.TryValidateEnumerable())
            {
                yield return new ValidationResult(
                    validationResult.ErrorMessage,
                    validationResult.MemberNames.Select(m => string.IsNullOrEmpty(baseKeyPath) ? m : $"{baseKeyPath}[{index}].{m}").ToArray());
            }

            yield break;
        }

        // 一般的なプロパティの検証
        Validator.TryValidateObject(targetClass, new ValidationContext(targetClass), validationResults, true);
        foreach (var validationResult in validationResults)
        {
            yield return new ValidationResult(
                validationResult.ErrorMessage,
                validationResult.MemberNames.Select(m => string.IsNullOrEmpty(baseKeyPath) ? m : $"{baseKeyPath}.{m}").ToArray());
        }

        static bool isReferenceType(Type type)
        {
            return type switch
            {
                Type t when t.IsPrimitive => false,
                Type t when t.IsEnum => false,
                Type t when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>) => false,
                Type t when t == typeof(string) => false,
                Type t when t == typeof(DateTime) => false,
                _ => true
            };
        }

        // 参照型のプロパティがあるかどうかをチェックする
        foreach (var pi in targetClass.GetType().GetProperties())
        {
            if (!isReferenceType(pi.PropertyType))
            {
                continue;
            }

            if (pi.PropertyType == typeof(Type))
            {
                continue;
            }

            var pvalue = pi.GetValue(targetClass);
            if (pvalue is null)
            {
                continue;
            }

            validationResults.Clear();
            if (pvalue is not null)
            {
                validationResults.AddRange(pvalue.TryValidateProperties(string.IsNullOrEmpty(baseKeyPath) ? pi.Name : $"{baseKeyPath}.{pi.Name}"));
            }
            foreach (var validationResult in validationResults)
            {
                yield return validationResult;
            }
        }
    }
}
