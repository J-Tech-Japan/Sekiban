using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
namespace Postgres.Sample.Domain.Aggregates.Players;

public record Player(string Name, Guid TeamId) : IAggregatePayload<Player>
{
    public static Player CreateInitialPayload(Player? _) => new(string.Empty, Guid.Empty);
}

