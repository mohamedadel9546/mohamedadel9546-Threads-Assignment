using Json.Models;
using Json.Shared;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Json.Repositories
{
    public class IncomingOrderRepository
    {
        private readonly JsonFileStore jsonFile;

        public IncomingOrderRepository(JsonFileStore jsonFile)
        {
            this.jsonFile = jsonFile;
        }

        public async Task<List<Order>> GetAllAsync(CancellationToken token)
        {
            await SimulatedDelay.WaitAsync("IncomingOrderRepository => Open Connection", 200, 200, token);
            await SimulatedDelay.WaitAsync("IncomingOrderRepository => Execute Query", 300, 600, token);

            var orders= await  jsonFile.ReadAllAsync<Order>(  DataFiles.IncomingOrders, token);
            Logging.Success($"IncomingOrderRepository -> Loaded {orders.Count} incoming orders.");
            return orders;
        }

        public List<Order> GetAll()
        {
            return jsonFile.ReadAll<Order>(DataFiles.IncomingOrders);
        }
    }
}
