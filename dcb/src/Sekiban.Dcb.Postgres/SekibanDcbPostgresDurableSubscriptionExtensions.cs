using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.Subscriptions;

namespace Sekiban.Dcb.Postgres;

/// <summary>PostgreSQL registration and owner provisioning for durable Orleans subscribers.</summary>
public static class SekibanDcbPostgresDurableSubscriptionExtensions
{
    /// <summary>
    ///     Registers the PostgreSQL durable state store. The default retains compatibility startup provisioning;
    ///     pass <paramref name="preProvisioned" /> after an owner has run the provisioning method to make runtime
    ///     state operations DML-only.
    /// </summary>
    public static IServiceCollection AddSekibanDcbPostgresDurableSubscriptions(
        this IServiceCollection services,
        bool preProvisioned = false)
    {
        services.TryAddSingleton<IDurableSubscriptionStore>(sp =>
            new PostgresDurableSubscriptionStore(
                sp.GetRequiredService<IDbContextFactory<SekibanDcbDbContext>>(),
                legacyAutoProvision: !preProvisioned));
        services.TryAddSingleton<IDurableSubscriptionNudgeFactory, NoOpDurableSubscriptionNudgeFactory>();
        return services;
    }

    /// <summary>Registers one opt-in durable subscriber and its hosted runner.</summary>
    public static IServiceCollection AddSekibanDcbPostgresDurableSubscription(
        this IServiceCollection services,
        Action<DurableSubscriptionOptions> configure,
        DurableSubscriptionHandler handler,
        bool preProvisioned = false)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(handler);

        var options = new DurableSubscriptionOptions();
        configure(options);
        options.Validate();

        var registration = new DurableSubscriptionRegistration(options, handler);
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(DurableSubscriptionRegistration) &&
                descriptor.ImplementationInstance is DurableSubscriptionRegistration existing &&
                existing.Options.Identity == options.Identity))
        {
            throw new InvalidOperationException(
                $"Durable subscription '{options.ServiceId}/{options.Name}' is already registered in this service container.");
        }

        services.AddSekibanDcbPostgresDurableSubscriptions(
            preProvisioned || options.ProvisioningMode == DurableSubscriptionProvisioningMode.PreProvisioned);
        services.AddSingleton(registration);
        services.AddSingleton<DurableSubscriptionRunner>(sp =>
            new DurableSubscriptionRunner(
                registration,
                sp.GetRequiredService<IDurableSubscriptionStore>(),
                sp.GetRequiredService<Sekiban.Dcb.Storage.IEventStoreFactory>(),
                sp.GetRequiredService<Sekiban.Dcb.Domains.IEventTypes>(),
                sp.GetRequiredService<IDurableSubscriptionNudgeFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DurableSubscriptionRunner>>()));
        services.AddSingleton<IDurableSubscriptionHandle>(sp =>
            new DurableSubscriptionHandle(GetRegisteredRunner(sp, options.Identity)));
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp =>
            GetRegisteredRunner(sp, options.Identity));
        return services;
    }

    private static DurableSubscriptionRunner GetRegisteredRunner(
        IServiceProvider serviceProvider,
        DurableSubscriptionIdentity identity) =>
        serviceProvider.GetServices<DurableSubscriptionRunner>()
            .Single(runner => runner.Identity == identity);

    /// <summary>Owner-controlled raw-SQL provisioning for the durable subscriber state table.</summary>
    public static async Task ProvisionDurableSubscriptionSchemaAsync(
        this IDbContextFactory<SekibanDcbDbContext> contextFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await PostgresDurableSubscriptionSchema.ProvisionAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Owner convenience overload for applications that already have a service provider.</summary>
    public static Task ProvisionDurableSubscriptionSchemaAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default) =>
        serviceProvider.GetRequiredService<IDbContextFactory<SekibanDcbDbContext>>()
            .ProvisionDurableSubscriptionSchemaAsync(cancellationToken);
}
