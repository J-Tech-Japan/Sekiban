using System.Text.Json;
namespace Sekiban.Dcb;

/// <summary>
/// Interface for providing DcbDomainTypes
/// </summary>
public interface IDcbDomainTypesProvider
{
    static abstract DcbDomainTypes Generate(JsonSerializerOptions jsonSerializerOptions);
}