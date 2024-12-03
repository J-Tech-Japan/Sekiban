using System.Globalization;

namespace Sekiban.Infrastructure.IndexedDb;

internal static class DateTimeConverter
{
    public static DateTime ToDateTime(string iso8601) =>
        DateTime.ParseExact(iso8601, "o", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    public static string ToString(DateTime dateTime) =>
        TimeZoneInfo.ConvertTimeToUtc(dateTime).ToString("o");
}
