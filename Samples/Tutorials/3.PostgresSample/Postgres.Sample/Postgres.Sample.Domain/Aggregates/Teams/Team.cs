using Sekiban.Core.Aggregate;
namespace Postgres.Sample.Domain.Aggregates.Teams;

public record Team(string Name) : IAggregatePayload<Team>
{

    public static Team CreateInitialPayload(Team? _) => new(string.Empty);
}
