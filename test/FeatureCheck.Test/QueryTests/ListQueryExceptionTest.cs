using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing;
using System.IO;
using Xunit;
namespace FeatureCheck.Test.QueryTests;

public class ListQueryExceptionTest : UnifiedTest<FeatureCheckDependency>
{

    [Fact]
    public void ShouldThrowTest()
    { 
        ThenQueryThrowsAnException(new ClientLoyaltyPointExceptionTestListQuery.Parameter(2));
    }
    [Fact]
    public void ShouldThrowTest2()
    { 
        ThenQueryThrows<InvalidDataException>(new ClientLoyaltyPointExceptionTestListQuery.Parameter(2));
    }    
    [Fact]
    public void ShouldNotThrowTest()
    { 
        ThenQueryNotThrowsAnException(new ClientLoyaltyPointExceptionTestListQuery.Parameter(0));
    }

    [Fact]
    public void GetExceptionTest()
    {
        ThenQueryGetException(new ClientLoyaltyPointExceptionTestListQuery.Parameter(2), exception => Assert.IsType<InvalidDataException>(exception));
    }
    [Fact]
    public void GetExceptionTest2()
    {
        ThenQueryGetException<InvalidDataException>(new ClientLoyaltyPointExceptionTestListQuery.Parameter(2), exception => Assert.IsType<InvalidDataException>(exception));
    }}
