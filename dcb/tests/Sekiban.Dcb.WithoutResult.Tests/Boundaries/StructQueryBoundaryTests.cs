using Dcb.Domain.WithoutResult;
using Sekiban.Dcb.Boundaries;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.Queries;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Boundaries;

/// <summary>
///     <c>ISekibanExecutor.QueryAsync&lt;TResult&gt;</c> is constrained to <c>notnull</c>, which admits structs — so
///     a query that answers with an <c>int</c> is an ordinary, supported thing to write. It is also where
///     <c>UnwrapBox()</c> was at its most dangerous: a failed <c>ResultBox&lt;int&gt;</c> came back as <c>0</c>. A
///     count query that could not reach the store answered "zero", and nothing anywhere threw.
/// </summary>
public class StructQueryBoundaryTests
{
    private readonly ISekibanExecutor _executor = new InMemoryDcbExecutorForTesting(DomainType.GetDomainTypes(), new InMemoryEventStore(DomainType.GetDomainTypes().EventTypes));

    [Fact]
    public async Task FailedStructQuery_Throws_InsteadOfAnsweringZero()
    {
        // A query the domain does not know: the core throws.
        var query = new UnregisteredStudentCountQuery();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => _executor.QueryAsync(query));

        // Before this change this call returned 0 and the caller had no way to know the query never ran.
        Assert.IsNotType<SekibanBoundaryException>(thrown);
        Assert.Equal("ISekibanExecutor.QueryAsync", thrown.Data[BoundaryGuard.OperationDataKey]);
        Assert.Equal(nameof(UnregisteredStudentCountQuery), thrown.Data[BoundaryGuard.TargetDataKey]);
    }

    /// <summary>
    ///     The Orleans typed-result boundary, as the executor writes it:
    ///     <c>BoundaryGuard.Run(() =&gt; general.ToTypedResult&lt;TResult&gt;(), context)</c>. With a struct
    ///     <c>TResult</c> a cast failure must throw, not answer <c>0</c>.
    /// </summary>
    [Fact]
    public void OrleansTypedResult_StructMismatch_Throws_InsteadOfAnsweringZero()
    {
        var general = new QueryResultGeneral("not an int", typeof(string).FullName!, new UnregisteredStudentCountQuery());

        var thrown = Assert.ThrowsAny<Exception>(
            () => BoundaryGuard.Run(
                () => general.ToTypedResult<int>(),
                new BoundaryContext("ISekibanExecutor.QueryAsync", "StudentCount")));

        Assert.IsType<InvalidCastException>(thrown);
        Assert.Equal("ISekibanExecutor.QueryAsync", thrown.Data[BoundaryGuard.OperationDataKey]);
    }

    [Fact]
    public void OrleansTypedResult_StructMatch_IsUntouched()
    {
        var general = new QueryResultGeneral(42, typeof(int).FullName!, new UnregisteredStudentCountQuery());

        var value = BoundaryGuard.Run(
            () => general.ToTypedResult<int>(),
            new BoundaryContext("ISekibanExecutor.QueryAsync", "StudentCount"));

        Assert.Equal(42, value);
    }

    /// <summary>A struct-answering query the domain never registered, so executing it is a failure by construction.</summary>
    private record UnregisteredStudentCountQuery : IQueryCommon<int>;
}
