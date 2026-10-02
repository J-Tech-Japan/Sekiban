using Microsoft.Extensions.DependencyInjection;

namespace Sekiban.Dcb.TagConsistencyFence;

public enum TagConsistencyFenceMode
{
    Off,
    DeriveFromReservations
}

/// <summary>Opt-in durable fencing of reservation inputs. Unread tags remain unfenced; this is not a read-set guarantee.</summary>
public sealed class TagConsistencyFenceOptions
{
    public TagConsistencyFenceMode Mode { get; set; } = TagConsistencyFenceMode.Off;
}

public static class SekibanDcbTagConsistencyFenceExtensions
{
    /// <summary>Configures the non-Orleans executors. Orleans support is deferred to SEK-G104.</summary>
    public static IServiceCollection AddSekibanDcbTagConsistencyFence(
        this IServiceCollection services, Action<TagConsistencyFenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TagConsistencyFenceOptions();
        configure(options);
        if (!Enum.IsDefined(options.Mode))
            throw new ArgumentOutOfRangeException(nameof(configure), "Unknown tag consistency fence mode.");
        services.AddOptions<TagConsistencyFenceOptions>().Configure(o => o.Mode = options.Mode);
        services.AddSingleton(options);
        return services;
    }
}
