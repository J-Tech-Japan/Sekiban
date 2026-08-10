using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.MaterializedView;

/// <summary>
/// Validates the caller-owned materialized-view service identity before an executor performs I/O.
/// This helper only normalizes and compares identifiers; it never resolves or caches an event store.
/// </summary>
public static class MvServiceIdValidation
{
    public static string Validate(
        string? requestedServiceId,
        MvOptions options,
        IServiceIdProvider? legacyServiceIdProvider,
        string executorName)
    {
        var callerSuppliedServiceId = !string.IsNullOrWhiteSpace(requestedServiceId);
        var resolvedServiceId = requestedServiceId;
        if (string.IsNullOrWhiteSpace(resolvedServiceId))
        {
            resolvedServiceId = options.ServiceId;
        }

        if (string.IsNullOrWhiteSpace(resolvedServiceId))
        {
            resolvedServiceId = legacyServiceIdProvider?.GetCurrentServiceId();
        }

        if (string.IsNullOrWhiteSpace(resolvedServiceId))
        {
            throw new InvalidOperationException(
                $"{executorName} requires an explicit non-empty ServiceId. Configure MvOptions.ServiceId or pass the service id at the caller boundary.");
        }

        var normalizedServiceId = ServiceIdValidator.NormalizeAndValidate(resolvedServiceId);
        if (string.Equals(normalizedServiceId, DefaultServiceIdProvider.DefaultServiceId, StringComparison.Ordinal) &&
            !options.AllowDefaultServiceId)
        {
            throw new InvalidOperationException(
                $"{executorName} cannot use the implicit default ServiceId. Opt into the named single-service compatibility option AllowDefaultServiceId or provide an explicit non-default service.");
        }

        if (callerSuppliedServiceId && !string.IsNullOrWhiteSpace(options.ServiceId))
        {
            var configuredServiceId = ServiceIdValidator.NormalizeAndValidate(options.ServiceId);
            if (!string.Equals(configuredServiceId, normalizedServiceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{executorName} requested ServiceId '{normalizedServiceId}', but MvOptions is bound to '{configuredServiceId}'.");
            }
        }

        if (legacyServiceIdProvider is not null)
        {
            var legacyServiceId = ServiceIdValidator.NormalizeAndValidate(legacyServiceIdProvider.GetCurrentServiceId());
            if (!string.Equals(legacyServiceId, normalizedServiceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{executorName} requested ServiceId '{normalizedServiceId}', but the legacy aggregate event store is bound to '{legacyServiceId}'. Register IEventStoreFactory for service-scoped MV reads.");
            }
        }

        return normalizedServiceId;
    }
}
