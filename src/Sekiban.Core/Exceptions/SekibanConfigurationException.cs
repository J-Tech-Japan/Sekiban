namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when the configuration is invalid.
///     Example : AWS dynamo db table, region, access key, S3 bucket, etc.
/// </summary>
public class SekibanConfigurationException : Exception, ISekibanException
{
    public SekibanConfigurationException(string? message) : base(message)
    {
    }
}
