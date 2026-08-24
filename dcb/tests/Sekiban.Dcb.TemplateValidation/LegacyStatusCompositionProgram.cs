using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Sqlite;

namespace Sekiban.Dcb.TemplateValidation;

internal static class LegacyStatusCompositionProgram
{
    private const string ReaderTypeName = "Sekiban.Dcb.Actors.IProjectionStatusReader";
    private const string SerializedReaderTypeName = "Sekiban.Dcb.Actors.ISerializedProjectionStatusReader";

    public static int Main()
    {
        try
        {
            var outcomes = new[]
            {
                InspectProvider("PostgreSQL WithAspire", services => services.AddSekibanDcbPostgresWithAspire()),
                InspectProvider("Cosmos WithAspire", services => services.AddSekibanDcbCosmosDbWithAspire()),
                InspectProvider("SQLite", services => services.AddSekibanDcbSqlite(Path.Combine(Path.GetTempPath(), "template-validation-legacy.db"))),
                InspectProvider("DynamoDB", services => services.AddSekibanDcbDynamoDbWithAspire())
            };

            var failures = outcomes.Where(outcome => !outcome.PassesCurrentComposition).ToArray();
            if (failures.Length == 0)
            {
                Console.WriteLine("The 10.8.2 graph unexpectedly satisfied the current status-reader composition proof.");
                return 0;
            }

            Console.Error.WriteLine(
                "Legacy 10.8.2 four-provider composition failed as required: " +
                string.Join("; ", failures.Select(outcome =>
                    $"{outcome.Name} readers={outcome.ReaderDescriptors}, serialized={outcome.SerializedReaderDescriptors}")));
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Legacy 10.8.2 four-provider composition execution error: {exception}");
            return 2;
        }
    }

    private static ProviderOutcome InspectProvider(string name, Action<IServiceCollection> registerProvider)
    {
        var services = new ServiceCollection();
        registerProvider(services);
        return new ProviderOutcome(
            name,
            services.Count(descriptor => descriptor.ServiceType.FullName == ReaderTypeName),
            services.Count(descriptor => descriptor.ServiceType.FullName == SerializedReaderTypeName));
    }

    private sealed record ProviderOutcome(string Name, int ReaderDescriptors, int SerializedReaderDescriptors)
    {
        public bool PassesCurrentComposition => ReaderDescriptors == 1 && SerializedReaderDescriptors == 1;
    }
}
