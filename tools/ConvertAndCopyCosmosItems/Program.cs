// See https://aka.ms/new-console-template for more information
using Convert011To012;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Dependency;
using Sekiban.Core.Documents;
using Sekiban.Infrastructure.Cosmos;
using System.Reflection;
var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly());
var configuration = builder.Build();
var serviceCollection = new ServiceCollection();
serviceCollection.AddSingleton<IConfiguration>(configuration);
SekibanEventSourcingDependency.Register(serviceCollection, new EmptyDependencyDefinition());
serviceCollection.AddSekibanCosmosDB();
serviceCollection.AddLogging();
serviceCollection.AddTransient<EventsConverter>();
var ServiceProvider = serviceCollection.BuildServiceProvider();

var eventConverter = ServiceProvider.GetService<EventsConverter>();
if (eventConverter == null)
{
    Console.WriteLine("起動できません");
    return;
}
// var containerGroup = args.Contains("dissolvable") ? AggregateContainerGroup.Dissolvable : AggregateContainerGroup.Default;
// var containerGroup = AggregateContainerGroup.Dissolvable;
var containerGroup = AggregateContainerGroup.Default;
Console.WriteLine("start converting " + containerGroup);
// put true for converting to Hierarchical Partition Keys, false for just copy without change of Partition Key either Hierarchical or Single
var result = await eventConverter.StartConvertAsync(containerGroup, false, DocumentType.Event);
Console.WriteLine($"converted {result} items. Finished.");
