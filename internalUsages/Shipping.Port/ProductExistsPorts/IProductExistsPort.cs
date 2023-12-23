namespace Shipping.Port.ProductExistsPorts;

public interface IProductExistsPort
{
    public Task<bool> ProductExistsAsync(Guid productId);
}
