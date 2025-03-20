using AspireEventSample.ApiService.Aggregates.ReadModel;
using Orleans;

namespace AspireEventSample.ApiService.Grains;

public interface ICartEntityWriter : IEntityWriter<CartEntity>, IGrainWithStringKey
{
}
