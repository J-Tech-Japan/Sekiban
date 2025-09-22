using Xunit;
namespace Sekiban.Dcb.BlobStorage.AzureStorage.Unit;

[CollectionDefinition("AzuriteCollection")]
public class AzuriteCollection : ICollectionFixture<AzuriteTestFixture>
{
}