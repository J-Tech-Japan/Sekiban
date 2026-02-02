using System.Globalization;
using System.Reflection;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .Build();

var options = CliOptions.Parse(args);
if (options.ShowHelp)
{
    PrintUsage();
    return;
}

var connectionString = options.ConnectionString
    ?? config.GetConnectionString("SekibanDcbCosmos")
    ?? config.GetConnectionString("CosmosDb")
    ?? config.GetConnectionString("SekibanDcbCosmosDb")
    ?? config["ConnectionStrings:SekibanDcbCosmos"]
    ?? config["ConnectionStrings:CosmosDb"]
    ?? config["ConnectionStrings:SekibanDcbCosmosDb"];

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("Cosmos DB connection string is required.");
    PrintUsage();
    return;
}

var databaseName = options.DatabaseName
    ?? config["CosmosDb:DatabaseName"]
    ?? "SekibanDcb";

var eventsContainerName = options.EventsContainerName
    ?? config["CosmosDb:EventsContainerName"]
    ?? "events";

var tagsContainerName = options.TagsContainerName
    ?? config["CosmosDb:TagsContainerName"]
    ?? "tags";

var serviceId = options.ServiceId
    ?? config["CosmosDb:ServiceId"]
    ?? "default";

var outputDir = options.OutputDir
    ?? config["CosmosDb:OutputDir"]
    ?? "./cosmos-backup";

var maxConcurrency = options.MaxConcurrency
    ?? GetIntSetting(config, "CosmosDb:MaxConcurrency")
    ?? 20;

var throughput = options.Throughput
    ?? GetIntSetting(config, "CosmosDb:Throughput");

var autoscaleMax = options.AutoscaleMaxThroughput
    ?? GetIntSetting(config, "CosmosDb:AutoscaleMaxThroughput");

var confirm = options.Confirm || GetBoolSetting(config, "CosmosDb:Confirm");

Directory.CreateDirectory(outputDir);

var timestampSuffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
var eventsBackupPath = Path.Combine(outputDir, $"events-{timestampSuffix}.jsonl");
var tagsBackupPath = Path.Combine(outputDir, $"tags-{timestampSuffix}.jsonl");

var clientOptions = new CosmosClientOptions
{
    AllowBulkExecution = true
};

using var cosmosClient = new CosmosClient(connectionString, clientOptions);
var database = cosmosClient.GetDatabase(databaseName);

Console.WriteLine($"Cosmos DB: {databaseName}");
Console.WriteLine($"Events Container: {eventsContainerName}");
Console.WriteLine($"Tags Container: {tagsContainerName}");
Console.WriteLine($"ServiceId (default for legacy rows): {serviceId}");
Console.WriteLine($"Backup directory: {Path.GetFullPath(outputDir)}");
Console.WriteLine($"Max concurrency: {maxConcurrency}");

Console.WriteLine("\nStep 1/4: Export legacy data to JSONL...");
var eventsExported = await ExportContainerAsync(database, eventsContainerName, eventsBackupPath);
var tagsExported = await ExportContainerAsync(database, tagsContainerName, tagsBackupPath);
Console.WriteLine($"Export completed. events={eventsExported}, tags={tagsExported}");

if (!confirm)
{
    Console.WriteLine("\nDestructive steps are disabled. Re-run with --confirm to delete/recreate containers and upload.");
    return;
}

Console.WriteLine("\nStep 2/4: Delete containers...");
await DeleteContainerIfExistsAsync(database, eventsContainerName);
await DeleteContainerIfExistsAsync(database, tagsContainerName);

Console.WriteLine("\nStep 3/4: Recreate containers with /pk partition key...");
await CreateContainerAsync(database, eventsContainerName, throughput, autoscaleMax);
await CreateContainerAsync(database, tagsContainerName, throughput, autoscaleMax);

Console.WriteLine("\nStep 4/4: Convert and upload data...");
var eventsContainer = database.GetContainer(eventsContainerName);
var tagsContainer = database.GetContainer(tagsContainerName);

var eventsUploaded = await ImportJsonlAsync(
    eventsContainer,
    eventsBackupPath,
    line => ConvertEvent(line, serviceId),
    maxConcurrency);

var tagsUploaded = await ImportJsonlAsync(
    tagsContainer,
    tagsBackupPath,
    line => ConvertTag(line, serviceId),
    maxConcurrency);

Console.WriteLine($"Import completed. events={eventsUploaded}, tags={tagsUploaded}");

static async Task<int> ExportContainerAsync(Database database, string containerName, string outputPath)
{
    var container = database.GetContainer(containerName);
    var query = new QueryDefinition("SELECT * FROM c");
    var requestOptions = new QueryRequestOptions
    {
        MaxItemCount = 1000,
        MaxConcurrency = -1,
        MaxBufferedItemCount = -1
    };

    var total = 0;
    await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
    await using var writer = new StreamWriter(stream);

    using var iterator = container.GetItemQueryIterator<JObject>(query, requestOptions: requestOptions);
    while (iterator.HasMoreResults)
    {
        var response = await iterator.ReadNextAsync();
        foreach (var item in response)
        {
            await writer.WriteLineAsync(item.ToString(Formatting.None));
            total++;
            if (total % 1000 == 0)
            {
                Console.WriteLine($"  exported {total} from {containerName}");
            }
        }
    }

    return total;
}

static async Task DeleteContainerIfExistsAsync(Database database, string containerName)
{
    try
    {
        await database.GetContainer(containerName).DeleteContainerAsync();
        Console.WriteLine($"  deleted {containerName}");
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        Console.WriteLine($"  {containerName} not found (skip)");
    }
}

static async Task CreateContainerAsync(Database database, string containerName, int? throughput, int? autoscaleMax)
{
    var properties = new ContainerProperties(containerName, "/pk");
    ThroughputProperties? throughputProperties = null;

    if (autoscaleMax.HasValue && autoscaleMax.Value > 0)
    {
        throughputProperties = ThroughputProperties.CreateAutoscaleThroughput(autoscaleMax.Value);
    }
    else if (throughput.HasValue && throughput.Value > 0)
    {
        throughputProperties = ThroughputProperties.CreateManualThroughput(throughput.Value);
    }

    await database.CreateContainerIfNotExistsAsync(properties, throughputProperties);
    Console.WriteLine($"  created {containerName} with partition key /pk");
}

static async Task<int> ImportJsonlAsync(
    Container container,
    string inputPath,
    Func<string, JObject?> converter,
    int maxConcurrency)
{
    var total = 0;
    var skipped = 0;
    var batch = new List<Task>();

    foreach (var line in File.ReadLines(inputPath))
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        batch.Add(Task.Run(async () =>
        {
            var converted = converter(line);
            if (converted == null)
            {
                Interlocked.Increment(ref skipped);
                return;
            }

            var pk = converted["pk"]?.ToString();
            if (string.IsNullOrWhiteSpace(pk))
            {
                Interlocked.Increment(ref skipped);
                return;
            }

            await container.UpsertItemAsync(converted, new PartitionKey(pk));
            var current = Interlocked.Increment(ref total);
            if (current % 1000 == 0)
            {
                Console.WriteLine($"  imported {current} into {container.Id}");
            }
        }));

        if (batch.Count >= maxConcurrency)
        {
            await Task.WhenAll(batch);
            batch.Clear();
        }
    }

    if (batch.Count > 0)
    {
        await Task.WhenAll(batch);
    }

    if (skipped > 0)
    {
        Console.WriteLine($"  skipped {skipped} items for {container.Id}");
    }

    return total;
}

static JObject? ConvertEvent(string jsonLine, string defaultServiceId)
{
    JObject source;
    try
    {
        source = JObject.Parse(jsonLine);
    }
    catch (JsonException)
    {
        return null;
    }

    var id = GetString(source, "id", "Id");
    if (string.IsNullOrWhiteSpace(id))
    {
        return null;
    }

    var serviceId = GetString(source, "serviceId", "ServiceId") ?? defaultServiceId;
    var pk = GetString(source, "pk", "Pk") ?? $"{serviceId}|{id}";

    var sortableUniqueId = GetString(source, "sortableUniqueId", "SortableUniqueId") ?? string.Empty;
    var eventType = GetString(source, "eventType", "EventType") ?? string.Empty;

    var payloadToken = GetToken(source, "payload", "Payload", "payloadJson", "PayloadJson") ?? JValue.CreateString("{}");
    var payload = payloadToken.Type == JTokenType.String
        ? payloadToken.ToString()
        : payloadToken.ToString(Formatting.None);

    var tagsToken = GetToken(source, "tags", "Tags") ?? GetToken(source, "tagsJson", "TagsJson");
    var tags = NormalizeTags(tagsToken);

    var timestampToken = GetToken(source, "timestamp", "Timestamp") ?? ConvertUnixTimestamp(source["_ts"]);
    var causationId = GetString(source, "causationId", "CausationId");
    var correlationId = GetString(source, "correlationId", "CorrelationId");
    var executedUser = GetString(source, "executedUser", "ExecutedUser");

    var dest = new JObject
    {
        ["pk"] = pk,
        ["serviceId"] = serviceId,
        ["id"] = id,
        ["sortableUniqueId"] = sortableUniqueId,
        ["eventType"] = eventType,
        ["payload"] = payload,
        ["tags"] = tags,
        ["timestamp"] = timestampToken ?? JValue.CreateString(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
    };

    if (!string.IsNullOrWhiteSpace(causationId)) dest["causationId"] = causationId;
    if (!string.IsNullOrWhiteSpace(correlationId)) dest["correlationId"] = correlationId;
    if (!string.IsNullOrWhiteSpace(executedUser)) dest["executedUser"] = executedUser;

    return dest;
}

static JObject? ConvertTag(string jsonLine, string defaultServiceId)
{
    JObject source;
    try
    {
        source = JObject.Parse(jsonLine);
    }
    catch (JsonException)
    {
        return null;
    }

    var tag = GetString(source, "tag", "Tag");
    if (string.IsNullOrWhiteSpace(tag))
    {
        return null;
    }

    var serviceId = GetString(source, "serviceId", "ServiceId") ?? defaultServiceId;
    var pk = GetString(source, "pk", "Pk") ?? $"{serviceId}|{tag}";
    var id = GetString(source, "id", "Id") ?? Guid.NewGuid().ToString();
    var tagGroup = GetString(source, "tagGroup", "TagGroup") ?? BuildTagGroup(tag);

    var sortableUniqueId = GetString(source, "sortableUniqueId", "SortableUniqueId") ?? string.Empty;
    var eventType = GetString(source, "eventType", "EventType") ?? string.Empty;
    var eventId = GetString(source, "eventId", "EventId") ?? string.Empty;

    var createdAtToken = GetToken(source, "createdAt", "CreatedAt") ?? ConvertUnixTimestamp(source["_ts"]);

    var dest = new JObject
    {
        ["pk"] = pk,
        ["serviceId"] = serviceId,
        ["id"] = id,
        ["tag"] = tag,
        ["tagGroup"] = tagGroup,
        ["eventType"] = eventType,
        ["sortableUniqueId"] = sortableUniqueId,
        ["eventId"] = eventId,
        ["createdAt"] = createdAtToken ?? JValue.CreateString(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
    };

    return dest;
}

static string? GetString(JObject source, params string[] names)
{
    foreach (var name in names)
    {
        if (source.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                continue;
            }

            return token.ToString();
        }
    }

    return null;
}

static JToken? GetToken(JObject source, params string[] names)
{
    foreach (var name in names)
    {
        if (source.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                continue;
            }

            return token;
        }
    }

    return null;
}

static JArray NormalizeTags(JToken? token)
{
    if (token == null || token.Type == JTokenType.Null)
    {
        return new JArray();
    }

    if (token is JArray array)
    {
        return array;
    }

    if (token.Type == JTokenType.String)
    {
        var text = token.ToString();
        if (text.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                return JArray.Parse(text);
            }
            catch (JsonException)
            {
                return new JArray(text);
            }
        }

        return new JArray(text);
    }

    return new JArray(token.ToString());
}

static JToken? ConvertUnixTimestamp(JToken? token)
{
    if (token == null || token.Type == JTokenType.Null)
    {
        return null;
    }

    if (long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
    {
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }

    return null;
}

static string BuildTagGroup(string tag)
{
    var colonIndex = tag.IndexOf(':');
    return colonIndex > 0 ? tag[..colonIndex] : tag;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/MigrateDcbCosmosEventsTags -- [options]");
    Console.WriteLine("\nOptions:");
    Console.WriteLine("  --connection-string <value>   Cosmos DB connection string (required if not in config)");
    Console.WriteLine("  --database <value>            Cosmos database name (default: SekibanDcb)");
    Console.WriteLine("  --events-container <value>    Events container name (default: events)");
    Console.WriteLine("  --tags-container <value>      Tags container name (default: tags)");
    Console.WriteLine("  --service-id <value>          ServiceId to use when missing (default: default)");
    Console.WriteLine("  --output-dir <value>          Backup output directory (default: ./cosmos-backup)");
    Console.WriteLine("  --max-concurrency <value>     Max concurrent upserts (default: 20)");
    Console.WriteLine("  --throughput <value>          Manual RU/s for recreated containers (optional)");
    Console.WriteLine("  --autoscale-max <value>       Autoscale max RU/s for recreated containers (optional)");
    Console.WriteLine("  --confirm                     Delete/recreate containers and upload data");
}

static int? GetIntSetting(IConfiguration config, string key)
{
    var value = config[key];
    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
}

static bool GetBoolSetting(IConfiguration config, string key)
{
    var value = config[key];
    return bool.TryParse(value, out var result) && result;
}

sealed class CliOptions
{
    public string? ConnectionString { get; init; }
    public string? DatabaseName { get; init; }
    public string? EventsContainerName { get; init; }
    public string? TagsContainerName { get; init; }
    public string? ServiceId { get; init; }
    public string? OutputDir { get; init; }
    public int? MaxConcurrency { get; init; }
    public int? Throughput { get; init; }
    public int? AutoscaleMaxThroughput { get; init; }
    public bool Confirm { get; init; }
    public bool ShowHelp { get; init; }

    public static CliOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            if (string.Equals(key, "help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "h", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("help");
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[i + 1];
                i++;
            }
            else
            {
                flags.Add(key);
            }
        }

        return new CliOptions
        {
            ConnectionString = Get(values, "connection-string"),
            DatabaseName = Get(values, "database"),
            EventsContainerName = Get(values, "events-container"),
            TagsContainerName = Get(values, "tags-container"),
            ServiceId = Get(values, "service-id"),
            OutputDir = Get(values, "output-dir"),
            MaxConcurrency = ParseInt(values, "max-concurrency"),
            Throughput = ParseInt(values, "throughput"),
            AutoscaleMaxThroughput = ParseInt(values, "autoscale-max"),
            Confirm = flags.Contains("confirm"),
            ShowHelp = flags.Contains("help")
        };
    }

    private static string? Get(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static int? ParseInt(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
}
