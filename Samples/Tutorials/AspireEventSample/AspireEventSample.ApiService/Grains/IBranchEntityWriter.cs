using AspireEventSample.ReadModels;
using Sekiban.Pure.Orleans.ReadModels;
namespace AspireEventSample.ApiService.Grains;

public interface IBranchEntityWriter : IEntityWriter<BranchDbRecord>, IGrainWithStringKey
{
}
