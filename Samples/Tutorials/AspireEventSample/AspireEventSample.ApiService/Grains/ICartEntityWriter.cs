using AspireEventSample.ApiService.Aggregates.ReadModel;
using Sekiban.Pure.Orleans.ReadModels;
namespace AspireEventSample.ApiService.Grains;

public interface ICartEntityWriter : IEntityWriter<CartEntity>, IGrainWithStringKey
{
}