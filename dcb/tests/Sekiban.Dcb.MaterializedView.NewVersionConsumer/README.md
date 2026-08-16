# Materialized-view new-version consumer

This consumer is compiled against the current source and proves the additive public value
`VerifyAndExecute = 2` through both `MvOptions` mode properties. It also consumes the public
`MvVerifiedExecutionConfigurationException` / `MvTransitionNotAllowedException` refusal surface.

The execution-path and no-side-effect guarantees are covered by the Materialized View unit and
real-PostgreSQL tests; this fixture protects the public API shape available to a newly compiled consumer.
