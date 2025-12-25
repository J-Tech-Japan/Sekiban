using System.Text.Json;
namespace Sekiban.Pure;

public interface ISekibanDomainTypesProvider
{
    static abstract SekibanDomainTypes Generate(JsonSerializerOptions jsonSerializerOptions);
}
