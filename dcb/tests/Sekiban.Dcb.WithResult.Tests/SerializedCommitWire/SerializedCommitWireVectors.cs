using System.Text;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     Shared frozen wire vectors + canonical dataset for the SEK-G17 serialized-commit contract, so the golden byte tests
///     and the end-to-end real-executor test assert against the SAME literal 10.1.17 bytes (see provenance on
///     <see cref="SerializedCommitWireGoldenTests" />).
/// </summary>
internal static class SerializedCommitWireVectors
{
    // Base64 of the exact UTF-8 wire bytes. Decoded JSON (camelCase, non-ASCII escaped, base64 payloads):
    //   {"eventCandidates":[{"payload":"eyJhbW91bnQiOjEwMCwibWVtbyI6ImNhZsOpIn0=","eventPayloadName":"注文作成済","tags":["Cliente:José","Región:Sur"]},{"payload":"AAEC//4=","eventPayloadName":"OrderShipped","tags":["Cliente:José"]}],"consistencyTags":[{"tag":"Cliente:José","lastSortableUniqueId":""}]}
    public const string OfficialCamelBase64 =
        "eyJldmVudENhbmRpZGF0ZXMiOlt7InBheWxvYWQiOiJleUpoYlc5MWJuUWlPakV3TUN3aWJXVnRieUk2SW1OaFpzT3BJbjA9IiwiZXZlbnRQYXlsb2FkTmFtZSI6Ilx1NkNFOFx1NjU4N1x1NEY1Q1x1NjIxMFx1NkUwOCIsInRhZ3MiOlsiQ2xpZW50ZTpKb3NcdTAwRTkiLCJSZWdpXHUwMEYzbjpTdXIiXX0seyJwYXlsb2FkIjoiQUFFQy8vND0iLCJldmVudFBheWxvYWROYW1lIjoiT3JkZXJTaGlwcGVkIiwidGFncyI6WyJDbGllbnRlOkpvc1x1MDBFOSJdfV0sImNvbnNpc3RlbmN5VGFncyI6W3sidGFnIjoiQ2xpZW50ZTpKb3NcdTAwRTkiLCJsYXN0U29ydGFibGVVbmlxdWVJZCI6IiJ9XX0=";

    public const string VersionedV1CamelBase64 =
        "eyJ2ZXJzaW9uIjoxLCJldmVudENhbmRpZGF0ZXMiOlt7InBheWxvYWQiOiJleUpoYlc5MWJuUWlPakV3TUN3aWJXVnRieUk2SW1OaFpzT3BJbjA9IiwiZXZlbnRQYXlsb2FkTmFtZSI6Ilx1NkNFOFx1NjU4N1x1NEY1Q1x1NjIxMFx1NkUwOCIsInRhZ3MiOlsiQ2xpZW50ZTpKb3NcdTAwRTkiLCJSZWdpXHUwMEYzbjpTdXIiXX0seyJwYXlsb2FkIjoiQUFFQy8vND0iLCJldmVudFBheWxvYWROYW1lIjoiT3JkZXJTaGlwcGVkIiwidGFncyI6WyJDbGllbnRlOkpvc1x1MDBFOSJdfV0sImNvbnNpc3RlbmN5VGFncyI6W3sidGFnIjoiQ2xpZW50ZTpKb3NcdTAwRTkiLCJsYXN0U29ydGFibGVVbmlxdWVJZCI6IiJ9XX0=";

    public const string OfficialFreshPascalBase64 =
        "eyJFdmVudENhbmRpZGF0ZXMiOlt7IlBheWxvYWQiOiJleUpoYlc5MWJuUWlPakV3TUN3aWJXVnRieUk2SW1OaFpzT3BJbjA9IiwiRXZlbnRQYXlsb2FkTmFtZSI6Ilx1NkNFOFx1NjU4N1x1NEY1Q1x1NjIxMFx1NkUwOCIsIlRhZ3MiOlsiQ2xpZW50ZTpKb3NcdTAwRTkiLCJSZWdpXHUwMEYzbjpTdXIiXX0seyJQYXlsb2FkIjoiQUFFQy8vND0iLCJFdmVudFBheWxvYWROYW1lIjoiT3JkZXJTaGlwcGVkIiwiVGFncyI6WyJDbGllbnRlOkpvc1x1MDBFOSJdfV0sIkNvbnNpc3RlbmN5VGFncyI6W3siVGFnIjoiQ2xpZW50ZTpKb3NcdTAwRTkiLCJMYXN0U29ydGFibGVVbmlxdWVJZCI6IiJ9XX0=";

    public static byte[] OfficialCamel => Convert.FromBase64String(OfficialCamelBase64);
    public static byte[] VersionedV1Camel => Convert.FromBase64String(VersionedV1CamelBase64);
    public static byte[] OfficialFreshPascal => Convert.FromBase64String(OfficialFreshPascalBase64);

    public static readonly byte[] Payload1 = Encoding.UTF8.GetBytes("{\"amount\":100,\"memo\":\"café\"}");
    public static readonly byte[] Payload2 = { 0x00, 0x01, 0x02, 0xFF, 0xFE };

    public static SerializedCommitRequest BuildOfficial() =>
        new(
            new List<SerializableEventCandidate>
            {
                new(Payload1, "注文作成済", new List<string> { "Cliente:José", "Región:Sur" }),
                new(Payload2, "OrderShipped", new List<string> { "Cliente:José" })
            },
            new List<ConsistencyTagEntry> { new("Cliente:José", "") });

    public static VersionedSerializedCommitRequest BuildVersioned()
    {
        var official = BuildOfficial();
        return new VersionedSerializedCommitRequest(
            VersionedSerializedCommitRequest.CurrentVersion, official.EventCandidates, official.ConsistencyTags);
    }
}
