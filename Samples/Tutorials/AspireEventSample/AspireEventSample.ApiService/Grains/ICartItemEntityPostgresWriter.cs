using AspireEventSample.ReadModels;
using AspireEventSample.ApiService.Aggregates.ReadModel;
using Orleans.Runtime;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AspireEventSample.ApiService.Grains;

public interface ICartItemEntityPostgresWriter : IReadModelAccessor<CartItemDbRecord>, IGrainWithStringKey
{
    Task<List<CartItemDbRecord>> GetItemsByCartIdAsync(Guid cartId);
}
