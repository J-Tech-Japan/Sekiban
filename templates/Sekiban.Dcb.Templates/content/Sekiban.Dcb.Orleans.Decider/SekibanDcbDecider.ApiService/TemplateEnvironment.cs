namespace SekibanDcbDecider.ApiService;

public static class TemplateEnvironment
{
    public static bool IsDevelopment(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
}
