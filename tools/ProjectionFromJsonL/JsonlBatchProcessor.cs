using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Shared;
using System.Text.Json;
public static class JsonlBatchProcessor
{
    public static async Task RunAsync(
        string path,
        int batchSize,
        Func<IReadOnlyList<string>, CancellationToken, Task> processBatch,
        CancellationToken ct = default)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            // 必要なら AllowTrailingCommas などを設定
        };

        var batch = new List<string>(batchSize);

        await foreach (var line in File.ReadLinesAsync(path, ct)) // .NET 8+
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            batch.Add(line);


            if (batch.Count == batchSize)
            {
                await processBatch(batch, ct).ConfigureAwait(false);
                batch.Clear();                   // オブジェクトを解放
            }
        }

        // 端数が残っていれば処理
        if (batch.Count > 0)
        {
            await processBatch(batch, ct).ConfigureAwait(false);
        }
    }
}

public class EventProcessor
{
    public static IEvent ProcessEvent(JsonElement item, RegisteredEventTypes registeredEventTypes)
    {
            // pick out one item
            if (SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string typeName)
            {
                throw new InvalidOperationException($"Failed to deserialize event: {item}");
            }

            var toAdd = (registeredEventTypes
                        .RegisteredTypes
                        .Where(m => m.Name == typeName)
                        .Select(m => SekibanJsonHelper.ConvertTo(item, typeof(Event<>).MakeGenericType(m)) as IEvent)
                        .FirstOrDefault(m => m is not null) ??
                    EventHelper.GetUnregisteredEvent(item)) ??
                throw new SekibanUnregisteredEventFoundException();
            return toAdd;
    }

}
