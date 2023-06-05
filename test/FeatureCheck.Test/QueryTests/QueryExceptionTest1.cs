using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing;
using System.IO;
using Xunit;
namespace FeatureCheck.Test.QueryTests;

public class QueryExceptionTest1 : UnifiedTest<FeatureCheckDependency>
{

    [Fact]
    public void ShouldThrowTest()
    { 
        ThenQueryThrowsAnException(new ClientLoyaltyPointExceptionTestQuery.Parameter(2));
    }
    [Fact]
    public void ShouldThrowTest2()
    { 
        ThenQueryThrows<InvalidDataException>(new ClientLoyaltyPointExceptionTestQuery.Parameter(2));
    }    
    [Fact]
    public void ShouldNotThrowTest()
    { 
        ThenQueryNotThrowsAnException(new ClientLoyaltyPointExceptionTestQuery.Parameter(0));
    }

    [Fact]
    public void GetExceptionTest()
    {
        ThenQueryGetException(new ClientLoyaltyPointExceptionTestQuery.Parameter(2), exception => Assert.IsType<InvalidDataException>(exception));
    }
    [Fact]
    public void GetExceptionTest2()
    {
        ThenQueryGetException<InvalidDataException>(new ClientLoyaltyPointExceptionTestQuery.Parameter(2), exception => Assert.IsType<InvalidDataException>(exception));
    }
}
