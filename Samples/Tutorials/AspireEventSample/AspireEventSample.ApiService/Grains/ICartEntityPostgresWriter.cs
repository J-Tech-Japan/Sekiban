using AspireEventSample.ApiService.Aggregates.ReadModel;
using AspireEventSample.ReadModels;
namespace AspireEventSample.ApiService.Grains;

public interface ICartEntityPostgresWriter : IReadModelAccessor<CartDbRecord>, IGrainWithStringKey
{
}