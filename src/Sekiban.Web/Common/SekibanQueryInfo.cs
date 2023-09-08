namespace Sekiban.Web.Common;

/// <summary>
///     Sekiban query information
/// </summary>
public record SekibanQueryInfo
{
    /// <summary>
    ///     HTTP method name
    /// </summary>
    public string Method { get; init; } = string.Empty;
    /// <summary>
    ///     Get url
    /// </summary>
    public string GetUrl { get; init; } = string.Empty;
    /// <summary>
    ///     Get all events url
    /// </summary>
    public string GetEventsUrl { get; init; } = string.Empty;
    /// <summary>
    ///     Get all commands url
    /// </summary>
    public string GetCommandsUrl { get; init; } = string.Empty;
    /// <summary>
    ///     Sample response object
    /// </summary>
    public dynamic SampleResponseObject { get; init; } = string.Empty;
}
