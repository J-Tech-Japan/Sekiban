using System.Reflection;

namespace Sekiban.Dcb.MaterializedView.ApiSurfaceVerifier;

/// <summary>Minimum additive SEK-G103 surface, alongside the published assembly compatibility baseline.</summary>
internal static class GuardApiBaseline
{
    public static void Verify(Assembly candidate)
    {
        const string ns = "Sekiban.Dcb.MaterializedView.";
        var outcome = candidate.GetType(ns + "MvCatchUpOutcome", throwOnError: true)!;
        if (!Equals(outcome.GetField("Superseded")?.GetRawConstantValue(), 7))
        {
            throw new InvalidOperationException("The additive Superseded = 7 baseline is missing.");
        }

        var registry = candidate.GetType(ns + "IMvRegistryStore", throwOnError: true)!;
        var capability = registry.GetProperty("SupportsApplyLocking");
        if (capability?.PropertyType.FullName != "System.Boolean" ||
            capability?.GetMethod is not { IsAbstract: false })
            throw new InvalidOperationException("The default SupportsApplyLocking capability is missing.");

        var method = registry.GetMethod("LockEntriesForApplyAsync");
        var expectedParameters = new[]
        {
            "System.String", "System.String", "System.Int32", "System.Data.IDbTransaction", "System.Threading.CancellationToken"
        };
        if (method is null || method.IsAbstract ||
            !method.GetParameters().Select(parameter => parameter.ParameterType.FullName).SequenceEqual(expectedParameters) ||
            method.GetParameters()[^1].IsOptional != true ||
            method.ReturnType.GetGenericTypeDefinition().FullName != "System.Threading.Tasks.Task`1" ||
            method.ReturnType.GenericTypeArguments[0].GetGenericTypeDefinition().FullName != "System.Collections.Generic.IReadOnlyList`1" ||
            method.ReturnType.GenericTypeArguments[0].GenericTypeArguments[0].FullName != ns + "MvRegistryEntry")
        {
            throw new InvalidOperationException("The default-implemented LockEntriesForApplyAsync baseline changed.");
        }
    }
}
