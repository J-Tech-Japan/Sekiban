using AspireEventSample.ReadModels;
using Sekiban.Pure.Orleans.ReadModels;
namespace AspireEventSample.ApiService.Grains;

public interface IBranchWriter : IEntityWriter<BranchDbRecord>
{

}