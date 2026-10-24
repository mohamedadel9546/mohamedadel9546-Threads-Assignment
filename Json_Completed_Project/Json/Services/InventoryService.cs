using Json.Models;
using Json.Repositories;

namespace Json.Services;

public class InventoryService
{
    private readonly ProductRepository productRepository;
    public InventoryService(ProductRepository productRepository) => this.productRepository = productRepository;

    public async Task<bool> ReserveStockAsync(Order order, CancellationToken ct)
    {
        var reserved = new List<OrderItem>();
        try
        {
            foreach (var item in order.Items)
            {
                bool success = await productRepository.ReserveStockAsync(item.ProductId, item.Quantity, ct);
                if (!success)
                {
                    await RollbackAsync(reserved);
                    return false;
                }
                reserved.Add(item);
            }
            return true;
        }
        catch
        {
            // Roll back partial reservations even if the run token has been cancelled.
            await RollbackAsync(reserved);
            throw;
        }
    }

    private async Task RollbackAsync(IEnumerable<OrderItem> items)
    {
        foreach (var item in items)
            await productRepository.ReleaseStockAsync(item.ProductId, item.Quantity, CancellationToken.None);
    }

    public async Task ReleaseStockAsync(Order order, CancellationToken ct)
    {
        foreach (var item in order.Items)
            await productRepository.ReleaseStockAsync(item.ProductId, item.Quantity, ct);
    }
}
