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
var Configuration = builder.Build();
var ServiceCollection = new ServiceCollection();
ServiceCollection.AddSingleton<IConfiguration>(Configuration);
SekibanEventSourcingDependency.Register(ServiceCollection, new EmptyDependencyDefinition());
ServiceCollection.AddSekibanCosmosDB();
ServiceCollection.AddTransient<EventsConverter>();
var ServiceProvider = ServiceCollection.BuildServiceProvider();

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
