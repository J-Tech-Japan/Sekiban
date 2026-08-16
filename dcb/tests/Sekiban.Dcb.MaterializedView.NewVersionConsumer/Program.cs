using Sekiban.Dcb.MaterializedView;

var options = new MvOptions
{
    InitializationMode = MvInitializationMode.VerifyAndExecute
};
options.InfrastructureMode = MvInfrastructureMode.VerifyAndExecute;

if ((int)options.InitializationMode != 2 ||
    (int)options.InfrastructureMode != 2)
{
    throw new InvalidOperationException("The VerifyAndExecute mode value must remain additive at 2.");
}

var refusal = new MvVerifiedExecutionConfigurationException(
    options.InitializationMode,
    MvTransition.Apply,
    new MvTransitionIdentity("new-version-consumer", "Orders", 1));
if (refusal is not MvTransitionNotAllowedException ||
    refusal.Reason != MvTransitionNotAllowedReason.VerifiedExecutionPolicyRequired ||
    refusal.Transition != MvTransition.Apply)
{
    throw new InvalidOperationException("The VerifyAndExecute typed refusal contract changed.");
}

Console.WriteLine($"new-version-consumer-ok:{(int)options.InitializationMode}:{refusal.Reason}");
