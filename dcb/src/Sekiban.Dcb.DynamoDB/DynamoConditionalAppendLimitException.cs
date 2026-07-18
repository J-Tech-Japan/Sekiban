namespace Sekiban.Dcb.DynamoDB;

/// <summary>
///     Typed, fail-closed error raised BEFORE any DynamoDB call when a conditional (unique-key) append would violate a
///     TransactWriteItems limit: more than <see cref="MaxTransactItems" /> items (1 event + N tag rows), or a duplicate
///     item key (DynamoDB rejects a transaction that touches the same item more than once). Failing before the network
///     keeps the failure deterministic and prevents a partial/rejected transaction; it is a permanent request error, not a
///     retryable in-doubt and never a claim conflict.
/// </summary>
public sealed class DynamoConditionalAppendLimitException : Exception
{
    /// <summary>DynamoDB's hard TransactWriteItems item cap.</summary>
    public const int MaxTransactItems = 100;

    public DynamoConditionalAppendLimitException(string message) : base(message)
    {
    }
}
