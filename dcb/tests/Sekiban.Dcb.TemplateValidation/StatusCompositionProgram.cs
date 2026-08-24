using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.TemplateValidation;

internal static class StatusCompositionProgram
{
    public static int Main(string[] args)
    {
        try
        {
            VerifyProvider("PostgreSQL WithAspire", services => services.AddSekibanDcbPostgresWithAspire());
            VerifyProvider("Cosmos WithAspire", services => services.AddSekibanDcbCosmosDbWithAspire(options =>
                options.WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward));
            VerifyProvider("SQLite", services => services.AddSekibanDcbSqlite(Path.Combine(Path.GetTempPath(), "template-validation.db")));
            VerifyProvider("DynamoDB", services => services.AddSekibanDcbDynamoDbWithAspire());
            Assert(new MvOptions().InitializationMode == MvInitializationMode.CreateOrEnsure,
                "MvOptions no longer defaults to CreateOrEnsure.");

            Console.WriteLine("All four provider extensions resolve exactly one status reader and serialized status reader.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Status composition validation failed: {exception.Message}");
            return 1;
        }
    }

    private static void VerifyProvider(string providerName, Action<IServiceCollection> registerProvider)
    {
        var services = new ServiceCollection();
        registerProvider(services);

        Assert(services.Count(descriptor => descriptor.ServiceType == typeof(IProjectionStatusReader)) == 1,
            $"{providerName} did not register exactly one IProjectionStatusReader descriptor.");
        Assert(services.Count(descriptor => descriptor.ServiceType == typeof(ISerializedProjectionStatusReader)) == 1,
            $"{providerName} did not register exactly one ISerializedProjectionStatusReader descriptor.");

        // The composition proof deliberately substitutes only deferred store dependencies. The provider extension has
        // already added the real reader registrations; resolving those registrations must not open a provider connection.
        services.RemoveAll(typeof(IEventStore));
        services.RemoveAll(typeof(IProjectionStatusStore));
        services.AddSingleton(CreateNullProxy<IEventStore>());
        services.AddSingleton(CreateNullProxy<IProjectionStatusStore>());

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        Assert(serviceProvider.GetServices<IProjectionStatusReader>().Count() == 1,
            $"{providerName} did not resolve exactly one IProjectionStatusReader.");
        Assert(serviceProvider.GetServices<ISerializedProjectionStatusReader>().Count() == 1,
            $"{providerName} did not resolve exactly one ISerializedProjectionStatusReader.");
    }

    private static T CreateNullProxy<T>() where T : class => DispatchProxy.Create<T, NullDispatchProxy>();

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private class NullDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType.IsValueType == true
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
    }
}
