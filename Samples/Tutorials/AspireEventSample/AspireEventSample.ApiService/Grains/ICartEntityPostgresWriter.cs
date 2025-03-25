using AspireEventSample.ApiService.ReadModel;
using AspireEventSample.ReadModels;
namespace AspireEventSample.ApiService.Grains;

public interface ICartEntityPostgresWriter : IReadModelAccessor<CartDbRecord>, IGrainWithStringKey
{
}