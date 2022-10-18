using Sekiban.Addon.Tenant.Globalization;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
namespace Sekiban.Addon.Tenant.Extensions;

public static class AttributeExtensions
{
    public static string? GetDescription<T>(this T customAttributeProvider, bool inherit = false) where T : ICustomAttributeProvider
    {
        return customAttributeProvider.GetLocalizedDescriptionAttribute(inherit)?.Description ??
            customAttributeProvider.GetDescriptionAttribute(inherit)?.Description ??
            customAttributeProvider.GetDisplayAttribute(inherit)?.Description;
    }

    public static string? GetMemberDescription(this Type type, string memberName, bool inherit = false)
    {
        return type.GetCustomAttributeProviderMember(memberName)?.GetLocalizedDescriptionAttribute(inherit)?.Description ??
            type.GetCustomAttributeProviderMember(memberName)?.GetDescriptionAttribute(inherit)?.Description ??
            type.GetCustomAttributeProviderMember(memberName)?.GetDisplayAttribute(inherit)?.Description;
    }

    public static DescriptionAttribute? GetDescriptionAttribute<T>(this T customAttributeProvider, bool inherit = false)
        where T : ICustomAttributeProvider
    {
        return customAttributeProvider.GetAttribute<T, DescriptionAttribute>(inherit);
    }

    public static ResourceDescriptionAttribute? GetLocalizedDescriptionAttribute<T>(this T customAttributeProvider, bool inherit = false)
        where T : ICustomAttributeProvider
    {
        return customAttributeProvider.GetAttribute<T, ResourceDescriptionAttribute>(inherit);
    }

    public static string? GetDisplayName<T>(this T customAttributeProvider, bool inherit = false) where T : ICustomAttributeProvider
    {
        return customAttributeProvider.GetLocalizedDisplayNameAttribute(inherit)?.DisplayName ??
            customAttributeProvider.GetDisplayNameAttribute(inherit)?.DisplayName ?? customAttributeProvider.GetDisplayAttribute(inherit)?.Name;
    }

    public static string? GetMemberDisplayName(this Type type, string memberName, bool inherit = false)
    {
        return type.GetCustomAttributeProviderMember(memberName)?.GetLocalizedDisplayNameAttribute(inherit)?.DisplayName ??
            type.GetCustomAttributeProviderMember(memberName)?.GetDisplayNameAttribute(inherit)?.DisplayName ??
            type.GetCustomAttributeProviderMember(memberName)?.GetDisplayAttribute(inherit)?.Name;
    }

    public static ResourceDisplayNameAttribute? GetLocalizedDisplayNameAttribute<T>(this T customAttributeProvider, bool inherit = false)
        where T : ICustomAttributeProvider
    {
        return customAttributeProvider.GetAttribute<T, ResourceDisplayNameAttribute>(inherit);
    }

    public static DisplayNameAttribute? GetDisplayNameAttribute<T>(this T customAttributeProvider, bool inherit = false)
        where T : ICustomAttributeProvider
    {
        return customAttributeProvider.GetAttribute<T, DisplayNameAttribute>(inherit);
    }

    public static DisplayAttribute? GetDisplayAttribute<T>(this T customAttributeProvider, bool inherit = false) where T : ICustomAttributeProvider
    {
        return customAttributeProvider.GetAttribute<T, DisplayAttribute>(inherit);
    }

    private static ICustomAttributeProvider? GetCustomAttributeProviderMember(this Type type, string memberName)
    {
        var memberInfos = type.GetMember(memberName);
        return memberInfos?.FirstOrDefault();
    }

    private static U? GetAttribute<T, U>(this T p, bool inherit) where T : ICustomAttributeProvider where U : Attribute
    {
        var attributes = p.GetCustomAttributes(inherit);
        return attributes?.FirstOrDefault(f => f.GetType() == typeof(U)) as U;
    }
}
