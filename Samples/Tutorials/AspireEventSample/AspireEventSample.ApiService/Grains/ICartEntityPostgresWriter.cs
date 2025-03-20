using AspireEventSample.ApiService.Aggregates.ReadModel;
using AspireEventSample.ReadModels;
using Orleans;

namespace AspireEventSample.ApiService.Grains;

public interface ICartEntityPostgresWriter : IEntityWriter<CartDbRecord>, IGrainWithStringKey
{
}
