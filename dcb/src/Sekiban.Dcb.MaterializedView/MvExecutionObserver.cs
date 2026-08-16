namespace Sekiban.Dcb.MaterializedView;

/// <summary>
///     Internal diagnostic seam for execution-path proofs. Notifications happen at the provider-command and successful
///     commit boundaries and never participate in authorization or execution decisions.
/// </summary>
internal interface IMvExecutionObserver
{
    void OnProjectorCommandExecutionAttempt(string sql);

    void OnTransactionCommitted();
}
