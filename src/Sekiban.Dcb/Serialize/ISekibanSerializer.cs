using System.Text.Json;
namespace Sekiban.Dcb.Serialize;

public interface ISekibanSerializer
{
    JsonSerializerOptions GetJsonSerializerOptions();
    string Serialize<T>(T json);
    T Deserialize<T>(string json);
}
