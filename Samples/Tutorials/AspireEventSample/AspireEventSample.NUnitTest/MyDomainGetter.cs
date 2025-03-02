using AspireEventSample.ApiService.Generated;
using Sekiban.Pure;
using Sekiban.Pure.Orleans.NUnit;
namespace AspireEventSample.NUnitTest;

public class MyDomainGetter : IDomainTypesGetter
{
    public SekibanDomainTypes GetDomainTypes() =>
        AspireEventSampleApiServiceDomainTypes.Generate(AspireEventSampleApiServiceEventsJsonContext.Default.Options);
}