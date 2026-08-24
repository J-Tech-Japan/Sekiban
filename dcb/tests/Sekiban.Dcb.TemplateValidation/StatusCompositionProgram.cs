using System.Reflection;
using System.Runtime.Loader;
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
            VerifyProvider("PostgreSQL", services => services.AddSekibanDcbPostgres("Host=localhost;Database=template_validation;Username=postgres;Password=postgres"));
            VerifyProvider("Cosmos", services => services.AddSekibanDcbCosmosDb("AccountEndpoint=https://localhost:8081/;AccountKey=template-validation"));
            VerifyProvider("SQLite", services => services.AddSekibanDcbSqlite(Path.Combine(Path.GetTempPath(), "template-validation.db")));
            VerifyProvider("DynamoDB", services => services.AddSekibanDcbDynamoDbWithAspire());
            Assert(new MvOptions().InitializationMode == MvInitializationMode.CreateOrEnsure,
                "MvOptions no longer defaults to CreateOrEnsure.");

            var legacyPath = GetOption(args, "--legacy-core-path");
            if (legacyPath is not null)
            {
                VerifyLegacy1018Absence(legacyPath);
            }

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

    private static void VerifyLegacy1018Absence(string legacyCorePath)
    {
        legacyCorePath = Path.GetFullPath(legacyCorePath);
        Assert(File.Exists(legacyCorePath), $"The frozen 10.8.2 Sekiban.Dcb.Core assembly was not restored: {legacyCorePath}");

        var loadContext = new AssemblyLoadContext("sek-g44-legacy-10-8-2", isCollectible: true);
        try
        {
            var legacyAssembly = loadContext.LoadFromAssemblyPath(legacyCorePath);
            var extensionType = legacyAssembly.GetType("Sekiban.Dcb.Actors.ProjectionStatusServiceCollectionExtensions", throwOnError: false);
            var registrationExists = extensionType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Any(method => method.Name == "AddSekibanDcbProjectionStatusReader") == true;
            Assert(!registrationExists,
                "The 10.8.2 frozen package unexpectedly contains AddSekibanDcbProjectionStatusReader; the negative did not distinguish the 10.19 provider composition.");
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static T CreateNullProxy<T>() where T : class => DispatchProxy.Create<T, NullDispatchProxy>();

    private static string? GetOption(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == name)
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

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
