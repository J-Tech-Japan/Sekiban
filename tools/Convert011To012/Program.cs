// See https://aka.ms/new-console-template for more information
using Convert011To012;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Dependency;
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
var containerGroup = args.Contains("dissolvable") ? AggregateContainerGroup.Dissolvable : AggregateContainerGroup.Default;
Console.WriteLine("start converting " + containerGroup);
var result = await eventConverter.StartConvert(containerGroup);
Console.WriteLine($"converted {result} events. Finished.");
