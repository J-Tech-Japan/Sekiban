using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
namespace Sekiban.Web.OpenApi;

public static class AttributeExtensions
{
    public static string? GetDisplayName<T>(this T customAttributeProvider, bool inherit = false) where T : ICustomAttributeProvider =>
        customAttributeProvider.GetDisplayNameAttribute(inherit)?.DisplayName ?? customAttributeProvider.GetDisplayAttribute(inherit)?.Name;

    public static string? GetDescription<T>(this T customAttributeProvider, bool inherit = false) where T : ICustomAttributeProvider =>
        customAttributeProvider.GetDescriptionAttribute(inherit)?.Description ?? customAttributeProvider.GetDisplayAttribute(inherit)?.Description;

    private static DisplayAttribute? GetDisplayAttribute<T>(this T customAttributeProvider, bool inherit = false)
        where T : ICustomAttributeProvider =>
        customAttributeProvider.GetAttribute<T, DisplayAttribute>(inherit);

    private static DisplayNameAttribute? GetDisplayNameAttribute<T>(this T customAttributeProvider, bool inherit = false)
        where T : ICustomAttributeProvider =>
        customAttributeProvider.GetAttribute<T, DisplayNameAttribute>(inherit);

    private static DescriptionAttribute? GetDescriptionAttribute<T>(this T customAttributeProvider, bool inherit = false)
        where T : ICustomAttributeProvider =>
        customAttributeProvider.GetAttribute<T, DescriptionAttribute>(inherit);

    private static U? GetAttribute<T, U>(this T p, bool inherit) where T : ICustomAttributeProvider where U : Attribute
    {
        var attributes = p.GetCustomAttributes(inherit);
        return attributes?.FirstOrDefault(f => f.GetType() == typeof(U) || f.GetType().BaseType == typeof(U)) as U;
    }
}
