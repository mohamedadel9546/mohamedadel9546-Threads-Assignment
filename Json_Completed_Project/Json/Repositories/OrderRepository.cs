using Json.Models;
using Json.Shared;
using Json.Shared.Exceptions_;

namespace Json.Repositories;

public class OrderRepository
{
    private readonly JsonFileStore jsonFile;
    private readonly HashSet<int> _failSaveForOrderIds;

    public OrderRepository(JsonFileStore jsonFile, IEnumerable<int>? failSaveForOrderIds = null)
    {
        this.jsonFile = jsonFile;
        _failSaveForOrderIds = failSaveForOrderIds?.ToHashSet() ?? new HashSet<int>();
    }

    public async Task SaveAsync(Order order, CancellationToken token)
    {
        await SimulatedDelay.WaitAsync("OrderRepository Open Connection", 200, 200, token);
        await SimulatedDelay.WaitAsync("OrderRepository Beginning Transaction", 100, 100, token);
        await SimulatedDelay.WaitAsync("OrderRepository Saving Order", 400, 400, token);

        // Inject the requested commit failure before the Completed order is persisted.
        if (order.Status == OrderStatus.Completed && _failSaveForOrderIds.Contains(order.Id))
            throw new DatabaseWriteException("Connection lost during commit");

        await jsonFile.UpdateAllAsync<Order>(DataFiles.Orders, orders =>
        {
            int index = orders.FindIndex(o => o.Id == order.Id);
            if (index >= 0) orders[index] = order;
            else orders.Add(order);
        }, token);

        await SimulatedDelay.WaitAsync("OrderRepository -> Committing transaction", 200, 200, token);
        Logging.Success($"OrderRepository -> Order #{order.Id} saved.");
    }

    public async Task<List<Order>> GetAllAsync(CancellationToken token)
    {
        await SimulatedDelay.WaitAsync("OrderRepository -> Open Connection", 200, 200, token);
        await SimulatedDelay.WaitAsync("OrderRepository -> Execute Query", 300, 600, token);
        return await jsonFile.ReadAllAsync<Order>(DataFiles.Orders, token);
    }

    public async Task<List<Order>> GetByStatusAsync(OrderStatus status, CancellationToken token)
    {
        var orders = await GetAllAsync(token);
        return orders.Where(o => o.Status == status).ToList();
    }
}
