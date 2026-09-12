using Microsoft.Extensions.Logging;
using Orleans;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.ServiceId;

internal static class Program
{
    public static void Main()
    {
        Console.WriteLine("Sekiban.Dcb.Orleans.LegacyConsumer constructor compatibility probe.");
    }

    // This method is intentionally not invoked. Its compilation is the isolated source-consumer proof for both
    // target frameworks: the four-argument call, typed legacy five-argument call, legacy null-literal call, and
    // the additive options call must all bind without ambiguity.
    private static void BindLegacyAndOptionsCalls(
        IClusterClient clusterClient,
        IStreamDestinationResolver resolver,
        DcbDomainTypes domainTypes,
        ILogger<OrleansEventPublisher> logger,
        IServiceIdProvider serviceIdProvider)
    {
        _ = new OrleansEventPublisher(clusterClient, resolver, domainTypes, logger);
        _ = new OrleansEventPublisher(clusterClient, resolver, domainTypes, logger, serviceIdProvider);
        _ = new OrleansEventPublisher(clusterClient, resolver, domainTypes, logger, null);
        _ = new OrleansEventPublisher(
            clusterClient,
            resolver,
            domainTypes,
            logger,
            serviceIdProvider,
            new OrleansEventPublisherOptions());
    }
}
