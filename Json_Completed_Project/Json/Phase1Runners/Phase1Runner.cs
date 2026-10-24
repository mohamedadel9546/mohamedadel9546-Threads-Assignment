using Json.Models;
using Json.Repositories;
using Json.Seeding;
using Json.Services;
using Json.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Json.Phase1Runners
{
    public class Phase1Runner
    {
        private readonly JsonFileStore _jsonFileStore;
        private readonly IncomingOrderRepository _incomingOrderRepository;
        private readonly OrderRepository _orderRepository;
        private readonly OrderValidator _orderValidator;

        public Phase1Runner(JsonFileStore jsonFileStore, IncomingOrderRepository incomingOrderRepository,
            OrderRepository orderRepository, OrderValidator orderValidator)
        {
            _jsonFileStore = jsonFileStore;
            _incomingOrderRepository = incomingOrderRepository;
            _orderRepository = orderRepository;
            _orderValidator = orderValidator;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            Logging.Info("Phase 1 started.");

            await DataSeeder.ResetAsync(ct);

            var syncorder = _incomingOrderRepository.GetAll();
            var stopwatch = Stopwatch.StartNew();
            foreach(var order in syncorder)
            {
                ct.ThrowIfCancellationRequested();
                _orderValidator.ValidateOrder(order);
            }
            stopwatch.Stop();
            var syncelapsed = stopwatch.Elapsed;
            int syncvalidated = syncorder.Count(c => c.Status == OrderStatus.Validated);
            int syncrejected = syncorder.Count(c => c.Status == OrderStatus.Rejected);
            Logging.Info(
           $"Sync finished in {syncelapsed.TotalMilliseconds:F0} ms. " +
           $"Validated: {syncvalidated}, Rejected: {syncrejected}");

            Console.WriteLine("===============================**&*&(*^*(*");


            await DataSeeder.ResetAsync(ct);
            var Asyncorder =await _incomingOrderRepository.GetAllAsync(ct);
            stopwatch.Restart();
            foreach (var order in Asyncorder)
            {
                ct.ThrowIfCancellationRequested();
             await  _orderValidator.ValidateOrderAsync(order,ct);
            }
            stopwatch.Stop();

            var Asyncelapsed = stopwatch.Elapsed;
            var saveorder =await _orderRepository.GetAllAsync(ct);
            int Asyncvalidated = saveorder.Count(c => c.Status == OrderStatus.Validated);
            int Asyncrejected = saveorder.Count(c => c.Status == OrderStatus.Rejected);

            Logging.Info("========== PHASE 1 SUMMARY ==========");
            Logging.Info($"Total incoming orders: {Asyncorder.Count}");
            Logging.Info($"Sync time:  {syncelapsed.TotalMilliseconds:F0} ms");
            Logging.Info($"Async time: {Asyncelapsed.TotalMilliseconds:F0} ms");
            Logging.Info($"Async saved orders: {saveorder.Count}");
            Logging.Info($"Validated: {Asyncvalidated}");
            Logging.Info($"Rejected: {Asyncrejected}");
            Logging.Info("=====================================");
        }

    }
}
