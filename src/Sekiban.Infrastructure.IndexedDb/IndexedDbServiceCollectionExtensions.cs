using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Core.Documents;
using Sekiban.Core.Setting;
using Sekiban.Infrastructure.IndexedDb.Databases;
using Sekiban.Infrastructure.IndexedDb.Documents;

namespace Sekiban.Infrastructure.IndexedDb;

/// <summary>
/// Add IndexedDB Services for Sekiban
/// </summary>
public static class IndexedDbServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddSekibanIndexedDb(this IHostApplicationBuilder builder) =>
        AddSekibanIndexedDb<BlazorJsRuntime>(builder);
    public static IHostApplicationBuilder AddSekibanIndexedDb<T>(this IHostApplicationBuilder builder)
        where T : class, ISekibanJsRuntime
    {
        AddSekibanIndexedDb<T>(builder.Services, builder.Configuration);
        return builder;
    }

    public static WebAssemblyHostBuilder AddSekibanIndexedDb(this WebAssemblyHostBuilder builder) =>
        AddSekibanIndexedDb<BlazorJsRuntime>(builder);
    public static WebAssemblyHostBuilder AddSekibanIndexedDb<T>(this WebAssemblyHostBuilder builder)
        where T : class, ISekibanJsRuntime
    {
        AddSekibanIndexedDb<T>(builder.Services, builder.Configuration);
        return builder;
    }

    public static SekibanIndexedDbOptionsServiceCollection AddSekibanIndexedDb(this IServiceCollection services, IConfiguration configuration) =>
        AddSekibanIndexedDb<BlazorJsRuntime>(services, configuration);
    public static SekibanIndexedDbOptionsServiceCollection AddSekibanIndexedDb<T>(this IServiceCollection services, IConfiguration configuration)
        where T : class, ISekibanJsRuntime
    {
        var options = SekibanIndexedDbOptions.FromConfiguration(configuration);
        return AddIndexedDbDependencies<T>(services, options);
    }

    public static SekibanIndexedDbOptionsServiceCollection AddIndexedDbDependencies<T>(
        IServiceCollection services,
        SekibanIndexedDbOptions indexedDbOptions
    )
        where T : class, ISekibanJsRuntime
    {
        services.AddSingleton(indexedDbOptions);

        services.AddTransient<IndexedDbFactory>();

        services.AddTransient<IDocumentPersistentWriter, IndexedDbDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, IndexedDbDocumentRepository>();

        services.AddTransient<IEventPersistentWriter, IndexedDbDocumentWriter>();
        services.AddTransient<IEventPersistentRepository, IndexedDbDocumentRepository>();

        services.AddTransient<IDocumentRemover, IndexedDbDocumentRemover>();

        services.AddTransient<IBlobAccessor, IndexedDbBlobAccessor>();

        services.AddSingleton<ISekibanJsRuntime, T>();

        return new SekibanIndexedDbOptionsServiceCollection(indexedDbOptions, services);
    }
}
