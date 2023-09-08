namespace Sekiban.Web.Common;

/// <summary>
///     Command Information
/// </summary>
public record SekibanCommandInfo
{
    /// <summary>
    ///     Method name
    /// </summary>
    public string Method { get; set; } = string.Empty;
    /// <summary>
    ///     Method url
    /// </summary>
    public string Url { get; init; } = string.Empty;
    /// <summary>
    ///     Json body type
    /// </summary>
    public string JsonBodyType { get; init; } = string.Empty;
    /// <summary>
    ///     Sample body object
    /// </summary>
    public dynamic SampleBodyObject { get; init; } = string.Empty;
    /// <summary>
    ///     Sample response object
    /// </summary>
    public dynamic SampleResponseObject { get; init; } = string.Empty;
}
