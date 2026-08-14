using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public sealed class MvInitializationCompatibilityTests
{
    [Fact]
    public void NewOptionsKeepTheCreateEnsureAndAllowAllCompatibilityDefaults()
    {
        var options = new MvOptions();

        Assert.Equal(MvInitializationMode.CreateOrEnsure, options.InitializationMode);
        Assert.Same(MvAllowAllSqlStatementPolicy.Instance, options.SqlStatementPolicy);
    }

    [Fact]
    public void LegacyApplyHostGetsAnEmptyAdditiveSchemaContract()
    {
        var host = new LegacyHost();
        var bindings = new MvTableBindings(host.ViewName, host.ViewVersion, new MvOptions());

        Assert.Empty(((IMvApplyHost)host).GetSchemaRequirements(bindings));
    }

    [Fact]
    public void DuplicateSchemaRequirementsFailClosedWithATypedFailure()
    {
        var result = MvSchemaRequirements.ValidateContract(
            [new MvTable("orders", "orders_table", "OrderView", 1)],
            [
                new MvSchemaTableRequirement("orders", "orders_table", [new("id", MvSchemaTypeFamily.String, false)], ["id"]),
                new MvSchemaTableRequirement("orders", "orders_table_2", [new("id", MvSchemaTypeFamily.String, false)], ["id"])
            ]);

        Assert.False(result.IsCompatible);
        Assert.Equal(MvInitializationFailureReason.MissingSchemaContract, result.Failure?.Reason);
    }

    [Fact]
    public void PolicyFailureDoesNotCopySqlOrParameterValues()
    {
        var exception = new MvSqlPolicyRejectedException(
            new MvSqlPolicyFailure(
                "blocked",
                "orders",
                "OrderView",
                1,
                MvSqlStatementPhase.Apply));

        Assert.DoesNotContain("SELECT", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MvSqlStatementPhase.Apply, exception.Failure.Phase);
    }

    private sealed class LegacyHost : IMvApplyHost
    {
        public string ViewName => "Legacy";
        public int ViewVersion => 1;
        public IReadOnlyList<string> LogicalTables => [];

        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(
            IMvTableBindings tables,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);

        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
            SerializableEvent ev,
            IMvTableBindings tables,
            IMvApplyQueryPort queryPort,
            string sortableUniqueId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
    }
}
