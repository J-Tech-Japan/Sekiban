using AspireEventSample.ApiService.Aggregates.ReadModel;
using Sekiban.Pure.Orleans.ReadModels;
namespace AspireEventSample.ApiService.Grains;

public interface IBranchEntityWriter : IEntityWriter<BranchEntity>, IGrainWithStringKey
{
}