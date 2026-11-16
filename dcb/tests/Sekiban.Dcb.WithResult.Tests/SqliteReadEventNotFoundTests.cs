using System.Text;
using System.Text.Json;
using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.Data.Sqlite;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Sqlite;
using Xunit;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     The 11 storage contract returns <c>null</c> only when no event with the id exists. A stored row that cannot be
///     read is a failure, reported with the same <see cref="KeyNotFoundException" /> the 10.x SQLite store returned.
/// </summary>
public class SqliteReadEventNotFoundTests : IDisposable
{
    private static readonly DcbDomainTypes Domain = DomainType.GetDomainTypes();
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sqlite-read-event-{Guid.NewGuid():N}.db");

    private static SerializableEvent Ev() => new(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new StudentCreated(Guid.NewGuid(), "n", 5), Domain.JsonSerializerOptions)),
        SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
        Guid.NewGuid(),
        new EventMetadata("causation", "correlation", "test"),
        ["Student:1"],
        nameof(StudentCreated));

    [Fact]
    public async Task MissingEvent_IsNull()
    {
        var store = new SqliteEventStore(_dbPath, Domain.EventTypes);
        await store.WriteSerializableEventsAsync([Ev()]);

        Assert.Null(await store.ReadSerializableEventAsync(Guid.NewGuid()));
        Assert.Null(await store.ReadEventAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("EventType", "UnregisteredEvent")]
    [InlineData("TagsJson", "not json")]
    [InlineData("PayloadJson", "not json")]
    public async Task ExistingRowThatCannotBeRead_IsAFailure(string column, string value)
    {
        var store = new SqliteEventStore(_dbPath, Domain.EventTypes);
        var written = Ev();
        await store.WriteSerializableEventsAsync([written]);
        await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE dcb_events SET {column} = @value WHERE Id = @id";
            command.Parameters.AddWithValue("@value", value);
            command.Parameters.AddWithValue("@id", written.Id.ToString());
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var serialized = await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ReadSerializableEventAsync(written.Id));
        Assert.Equal($"Event not found: {written.Id}", serialized.Message);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ReadEventAsync(written.Id));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
