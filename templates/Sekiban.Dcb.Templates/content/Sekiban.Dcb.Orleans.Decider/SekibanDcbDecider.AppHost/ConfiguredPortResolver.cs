internal static class ConfiguredPortResolver
{
    public static int Resolve(int defaultPort, params string[] envNames)
    {
        foreach (string envName in envNames)
        {
            string? value = Environment.GetEnvironmentVariable(envName);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!int.TryParse(value, out int port) || port is < 1 or > 65535)
            {
                throw new InvalidOperationException(
                    $"Environment variable '{envName}' must be a valid TCP port between 1 and 65535.");
            }

            return port;
        }

        return defaultPort;
    }
}
