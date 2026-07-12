using Newtonsoft.Json;
using Sekiban.Dcb.CosmosDb.Migration;
using Sekiban.Dcb.CosmosDb.Models;

namespace Sekiban.Dcb.CosmosDb.TagMigration;

/// <summary>
///     Writes the rows a run is about to delete to a file, before it deletes them.
///     Cosmos has no undo, so this file IS the recovery path. The rows are written as complete documents in
///     the same shape the tags container stores them, so restoring is a matter of creating them again — no
///     transformation, no reconstruction, nothing to get wrong at 3am.
///     If this throws, the service has not deleted anything: the backup is written first, on purpose.
/// </summary>
internal sealed class FileBackupWriter : ICosmosTagMigrationBackupWriter
{
    private readonly string _path;

    public FileBackupWriter(string path) => _path = path;

    public async Task WriteAsync(
        CosmosTagMigrationPlan plan,
        IReadOnlyList<CosmosTag> rowsToRemove,
        CancellationToken cancellationToken)
    {
        var backup = new CosmosTagMigrationBackup
        {
            ServiceId = plan.ServiceId,
            EventsContainer = plan.EventsContainer,
            TagsContainer = plan.TagsContainer,
            PlanFingerprint = plan.Fingerprint,
            Rows = rowsToRemove
        };

        // Newtonsoft, to match the serializer the Cosmos SDK uses for these documents: what comes out of here
        // is exactly what went into the container, so it can go straight back in.
        var json = JsonConvert.SerializeObject(backup, Formatting.Indented);

        await File.WriteAllTextAsync(_path, json, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
///     The backup file's shape: the lineage it came from, the plan it belonged to, and the rows themselves.
///     To restore, create each row back into the tags container of the named lineage.
/// </summary>
internal sealed class CosmosTagMigrationBackup
{
    [JsonProperty("serviceId")]
    public string ServiceId { get; set; } = string.Empty;

    [JsonProperty("eventsContainer")]
    public string EventsContainer { get; set; } = string.Empty;

    [JsonProperty("tagsContainer")]
    public string TagsContainer { get; set; } = string.Empty;

    [JsonProperty("planFingerprint")]
    public string PlanFingerprint { get; set; } = string.Empty;

    [JsonProperty("rows")]
    public IReadOnlyList<CosmosTag> Rows { get; set; } = Array.Empty<CosmosTag>();
}
