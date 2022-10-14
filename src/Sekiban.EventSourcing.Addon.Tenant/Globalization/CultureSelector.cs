using System.Globalization;
namespace Sekiban.EventSourcing.Addon.Tenant.Globalization;

public static class CultureSelector
{
    public static void JapaneseJapan()
    {
        Select("ja-JP");
    }
    public static void EnglishPhilippines()
    {
        Select("en-PH");
    }

    private static void Select(string culture)
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(culture);
    }
}
