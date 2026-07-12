using Sekiban.Dcb.CosmosDb.Repair;
namespace Sekiban.Dcb.CosmosDb.Sweep;

/// <summary>
///     The production runner: builds the repair service for a lineage and runs one pass.
///     This is the sweep's ONLY route to storage, and it leads to exactly one place —
///     <see cref="CosmosDbTagRepairService.RepairAsync" />, whose store seam has no delete and no replace.
///     That is what makes "the sweep cannot be configured into destructiveness" a property of the types
///     rather than a promise in a comment: there is nothing else here to call.
/// </summary>
internal sealed class CosmosTagRepairRunner : ITagRepairRunner
{
    private readonly CosmosDbTagRepairServiceFactory _factory;

    public CosmosTagRepairRunner(CosmosDbTagRepairServiceFactory factory) => _factory = factory;

    public async Task<CosmosTagRepairReport> RunAsync(
        string serviceId,
        CosmosTagRepairOptions options,
        CancellationToken cancellationToken)
    {
        var repair = await _factory.CreateAsync(serviceId).ConfigureAwait(false);
        return await repair.RepairAsync(options, cancellationToken).ConfigureAwait(false);
    }
}
