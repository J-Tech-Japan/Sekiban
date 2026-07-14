namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     What kind of runtime an executor actually runs on.
/// </summary>
public enum ExecutorRuntimeKind
{
    /// <summary>
    ///     The executor did not say. Treated as unsafe: in Production the guard fails closed on it, because an executor
    ///     that cannot state its runtime cannot be shown to be a real one.
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     In-process actors, no cluster, no distribution — a unit-test runtime. Nothing here survives the process or
    ///     coordinates with another one.
    /// </summary>
    TestingInProcess = 1,

    /// <summary>
    ///     A real distributed runtime (Orleans today): grains placed across a cluster, state coordinated between hosts.
    /// </summary>
    DistributedRuntime = 2
}

/// <summary>
///     What an executor says about itself when something asks at runtime.
/// </summary>
/// <param name="Runtime">The kind of runtime this executor actually uses.</param>
/// <param name="RuntimeName">
///     A human name for the banner and the failure message: "Orleans", "InMemory (in-process actors)". Never a
///     connection string.
/// </param>
public sealed record ExecutorRuntimeDescriptor(ExecutorRuntimeKind Runtime, string RuntimeName)
{
    /// <summary>For an executor whose runtime cannot be determined — e.g. a generic executor over an unknown accessor.</summary>
    public static ExecutorRuntimeDescriptor Unknown(string runtimeName) =>
        new(ExecutorRuntimeKind.Unknown, runtimeName);

    /// <summary>e.g. <c>Orleans (DistributedRuntime)</c>.</summary>
    public override string ToString() => $"{RuntimeName} ({Runtime})";
}

/// <summary>
///     Implemented by executors — and by the actor accessors they run on — that can state their own runtime.
///     Executors that are generic over an accessor answer by asking the accessor, so the answer describes the runtime
///     that will actually execute the command, not the class that was registered.
/// </summary>
public interface IExecutorRuntimeDescriptorProvider
{
    /// <summary>Describes this executor as it is actually composed, at the moment it is asked.</summary>
    ExecutorRuntimeDescriptor DescribeRuntime();
}
