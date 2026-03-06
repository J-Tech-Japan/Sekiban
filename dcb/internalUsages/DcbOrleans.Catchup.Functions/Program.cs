using Dcb.Domain.WithoutResult;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Postgres;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var domainTypes = DomainType.GetDomainTypes();

        services.AddSingleton(domainTypes);
        services.AddSekibanDcbPostgresWithAspire();
        services.AddSekibanDcbColdExport(
            context.Configuration,
            context.HostingEnvironment.ContentRootPath,
            addBackgroundService: false);
    })
    .Build();

host.Run();
