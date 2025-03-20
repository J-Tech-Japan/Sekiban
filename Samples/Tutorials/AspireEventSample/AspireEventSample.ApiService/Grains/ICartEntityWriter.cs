using AspireEventSample.ApiService.Aggregates.ReadModel;
namespace AspireEventSample.ApiService.Grains;

public interface ICartReadModelAccessor : IReadModelAccessor<CartEntity>, IGrainWithStringKey
{
}