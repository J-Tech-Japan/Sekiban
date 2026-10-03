# Materialized-view API surface verifier

The verifier compares the public/protected API exposed by the published 10.14.1 assembly and the current net9
assembly. It fails if a baseline type, member, enum value, or visible signature is removed or changed; newly added
API is allowed. CI runs it next to the compiled-and-not-rebuilt 10.14.1 binary consumer.

The additive SEK-G103 baseline also requires `MvCatchUpOutcome.Superseded = 7` and the default-implemented
`IMvRegistryStore.LockEntriesForApplyAsync(string, string, int, IDbTransaction, CancellationToken)` returning
`Task<IReadOnlyList<MvRegistryEntry>>`, plus the default-false `bool SupportsApplyLocking` capability. The published baseline still checks all existing protected executor signatures.
