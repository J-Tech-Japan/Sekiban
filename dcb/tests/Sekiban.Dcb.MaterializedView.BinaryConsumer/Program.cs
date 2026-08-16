using Sekiban.Dcb.MaterializedView;

var options = new MvOptions
{
    ServiceId = "binary-consumer"
};

var initializationModeZero = MvInitializationMode.CreateOrEnsure;
var initializationModeOne = MvInitializationMode.VerifyOnly;
var infrastructureModeZero = MvInfrastructureMode.EnsureAndInitialize;
var infrastructureModeOne = MvInfrastructureMode.VerifyOnly;
var initializationProperty = new MvOptions { InitializationMode = initializationModeOne };
var infrastructureProperty = new MvOptions { InfrastructureMode = infrastructureModeOne };
if ((int)initializationModeZero != 0 || (int)initializationModeOne != 1 ||
    (int)infrastructureModeZero != 0 || (int)infrastructureModeOne != 1 ||
    initializationProperty.InitializationMode != MvInitializationMode.VerifyOnly ||
    infrastructureProperty.InfrastructureMode != MvInfrastructureMode.VerifyOnly)
{
    throw new InvalidOperationException("The 10.14.1 materialized-view mode/property contract changed.");
}

Func<IMvExecutor, IMvApplyHost, Task<int>> applySignature =
    static (executor, host) => executor.ApplySerializableEventsAsync(host, []);
Func<IMvActivationExecutor, IMvApplyHost, Task<MvCheckpointTruth>> captureSignature =
    static (executor, host) => executor.CaptureTargetCheckpointAsync(host);

Console.WriteLine(
    $"binary-consumer-ok:{options.ServiceId}:{typeof(IMvExecutor).FullName}:{applySignature.Method.ReturnType.Name}:{captureSignature.Method.ReturnType.Name}");
