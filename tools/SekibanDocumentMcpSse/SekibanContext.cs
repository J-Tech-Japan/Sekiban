using System.Text.Json.Serialization;
namespace SekibanDocumentMcpSse;

/// <summary>
/// JSON serialization context
/// </summary>
[JsonSerializable(typeof(List<DocumentInfo>))]
[JsonSerializable(typeof(DocumentInfo))]
[JsonSerializable(typeof(List<NavigationItem>))]
[JsonSerializable(typeof(NavigationItem))]
[JsonSerializable(typeof(SectionContent))]
[JsonSerializable(typeof(List<SearchResult>))]
[JsonSerializable(typeof(SearchResult))]
[JsonSerializable(typeof(List<SekibanCodeSample>))]
[JsonSerializable(typeof(SekibanCodeSample))]
internal sealed partial class SekibanContext : JsonSerializerContext { }