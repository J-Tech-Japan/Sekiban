namespace Sekiban.Dcb.MaterializedView;

/// <summary>
///     Public operation boundary used when a materialized-view mode refuses a command. The values deliberately
///     describe a caller-visible operation rather than a provider implementation detail.
/// </summary>
public enum MvTransition
{
    Initialize = 0,
    VerifyInitialization = 1,
    Apply = 2,
    CatchUp = 3,
    CaptureTargetCheckpoint = 4,
    Activate = 5,
    ForceReverse = 6,
    Refresh = 7
}

/// <summary>Typed reason for a materialized-view transition refusal.</summary>
public enum MvTransitionNotAllowedReason
{
    VerifyOnly = 0,
    UnknownMode = 1,
    VerifiedExecutionPolicyRequired = 2
}

/// <summary>Secret-free identity attached to a transition failure.</summary>
public sealed record MvTransitionIdentity(string ServiceId, string ViewName, int ViewVersion)
{
    public static MvTransitionIdentity Unknown { get; } = new("unknown", "unknown", -1);
}

/// <summary>
///     Raised before any store, event-store, connection, or projector work when a command cannot mutate in the
///     configured materialized-view mode.
/// </summary>
public class MvTransitionNotAllowedException : InvalidOperationException
{
    public MvTransitionNotAllowedException(
        MvInitializationMode mode,
        MvTransition transition,
        MvTransitionNotAllowedReason reason,
        MvTransitionIdentity identity)
        : base(CreateMessage(mode, transition, reason, identity))
    {
        Mode = mode;
        Transition = transition;
        Reason = reason;
        Identity = identity;
    }

    /// <summary>The configured public initialization mode, including an unrecognized numeric value when applicable.</summary>
    public MvInitializationMode Mode { get; }

    /// <summary>The command that was refused.</summary>
    public MvTransition Transition { get; }

    /// <summary>The typed refusal reason.</summary>
    public MvTransitionNotAllowedReason Reason { get; }

    /// <summary>The exact service/view identity supplied at the public boundary.</summary>
    public MvTransitionIdentity Identity { get; }

    public string ServiceId => Identity.ServiceId;
    public string ViewName => Identity.ViewName;
    public int ViewVersion => Identity.ViewVersion;

    private static string CreateMessage(
        MvInitializationMode mode,
        MvTransition transition,
        MvTransitionNotAllowedReason reason,
        MvTransitionIdentity identity) =>
        $"Materialized-view transition '{transition}' is not allowed in mode '{(int)mode}' " +
        $"for service '{identity.ServiceId}', view '{identity.ViewName}/{identity.ViewVersion}' ({reason}).";
}

/// <summary>
///     A mode-2 configuration failure. It is also a transition refusal so callers that already handle
///     <see cref="MvTransitionNotAllowedException"/> retain a single typed boundary.
/// </summary>
public sealed class MvVerifiedExecutionConfigurationException : MvTransitionNotAllowedException
{
    public MvVerifiedExecutionConfigurationException(
        MvInitializationMode mode,
        MvTransition transition,
        MvTransitionIdentity identity)
        : base(mode, transition, MvTransitionNotAllowedReason.VerifiedExecutionPolicyRequired, identity)
    {
    }
}

/// <summary>
///     The sole mode-to-capability mapping. Callers must resolve it at a public boundary before touching a store,
///     host, event source, or provider connection; unknown numeric enum values therefore fail closed.
/// </summary>
internal sealed record MvModeCapabilities(
    bool RequiresVerification,
    bool AllowsInfrastructureEnsure,
    bool AllowsProjectorApply,
    bool AllowsLifecycleDml)
{
    public bool UsesReadOnlyInspection => RequiresVerification && !AllowsLifecycleDml;

    /// <summary>
    ///     Mode 2 has no infrastructure ownership but does execute projector DML. Its explicit policy precondition
    ///     lets the executor authorize the complete batch before the first DML/registry command is sent.
    /// </summary>
    public bool RequiresWholeBatchPolicyAuthorization => RequiresVerification && AllowsProjectorApply;

    public static MvModeCapabilities Resolve(
        MvInitializationMode mode,
        MvTransition transition,
        MvTransitionIdentity identity) =>
        mode switch
        {
            MvInitializationMode.CreateOrEnsure => new(
                RequiresVerification: false,
                AllowsInfrastructureEnsure: true,
                AllowsProjectorApply: true,
                AllowsLifecycleDml: true),
            MvInitializationMode.VerifyOnly => new(
                RequiresVerification: true,
                AllowsInfrastructureEnsure: false,
                AllowsProjectorApply: false,
                AllowsLifecycleDml: false),
            MvInitializationMode.VerifyAndExecute => new(
                RequiresVerification: true,
                AllowsInfrastructureEnsure: false,
                AllowsProjectorApply: true,
                AllowsLifecycleDml: true),
            _ => throw new MvTransitionNotAllowedException(
                mode,
                transition,
                MvTransitionNotAllowedReason.UnknownMode,
                identity)
        };

    public static MvModeCapabilities ResolveAndValidate(
        MvOptions options,
        MvTransition transition,
        MvTransitionIdentity identity)
    {
        var capabilities = Resolve(options.InitializationMode, transition, identity);
        if (capabilities.RequiresWholeBatchPolicyAuthorization &&
            (options.SqlStatementPolicyMode != MvSqlStatementPolicyMode.Enforced ||
             options.SqlStatementPolicy is null ||
             ReferenceEquals(options.SqlStatementPolicy, MvAllowAllSqlStatementPolicy.Instance)))
        {
            throw new MvVerifiedExecutionConfigurationException(options.InitializationMode, transition, identity);
        }

        return capabilities;
    }

    public static MvTransitionNotAllowedException CreateRefusal(
        MvInitializationMode mode,
        MvTransition transition,
        MvTransitionIdentity identity) =>
        new(mode, transition, MvTransitionNotAllowedReason.VerifyOnly, identity);
}
