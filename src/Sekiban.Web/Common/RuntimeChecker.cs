namespace Sekiban.Web.Common;

public static class RuntimeChecker
{
    public static bool IsDotNet9OrLater()
    {
        var version = Environment.Version;
        return version.Major >= 9;
    }
}
