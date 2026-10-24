using Json.Models;
using Json.Seeding;
using Json.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Json.Services
{
    using Json.Repositories;
    using System.Diagnostics;

    public class Phase2Runner
    {
        private readonly JsonFileStore _jsonFileStore;
        private readonly RaceConditionDemo _raceConditionDemo;
        private readonly OrderPricer _orderPricer;
        private readonly OrderValidator _orderValidator;
        private readonly OrderProcessorV2 _orderProcessorV2;
        private readonly ProductRepository _productRepository;

        public Phase2Runner(
            RaceConditionDemo raceConditionDemo,
            OrderPricer orderPricer,
            JsonFileStore jsonFileStore,
            OrderValidator orderValidator,
            OrderProcessorV2 orderProcessorV2,
            ProductRepository productRepository)
        {
            _raceConditionDemo = raceConditionDemo;
            _orderPricer = orderPricer;
            _jsonFileStore = jsonFileStore;
            _orderValidator = orderValidator;
            _orderProcessorV2 = orderProcessorV2;
            _productRepository = productRepository;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            // 1. Reset data
            await DataSeeder.ResetAsync(ct);

            // 2. Demonstrate the race condition
            await _raceConditionDemo.RunAsync(ct);

            // 3. Reset data again
            await DataSeeder.ResetAsync(ct);

            // 4. Compare sequential and concurrent pricing
            var incomingOrders =
                await _jsonFileStore.ReadAllAsync<Order>(
                    DataFiles.IncomingOrders, ct);

            var order1 = incomingOrders.FirstOrDefault(o => o.Id == 1)
                ?? throw new InvalidOperationException(
                    "Order #1 was not found.");

            var sequentialOrder = order1.DeepCopy();
            var concurrentOrder = order1.DeepCopy();

            var stopwatch = Stopwatch.StartNew();

            await _orderPricer.PriceOrderSequentialAsync(
                sequentialOrder, ct);

            stopwatch.Stop();

            Logging.Info(
                $"Sequential pricing: {stopwatch.ElapsedMilliseconds} ms");

            stopwatch.Restart();

            await _orderPricer.PriceOrderAsync(
                concurrentOrder, ct);

            stopwatch.Stop();

            Logging.Info(
                $"Concurrent pricing: {stopwatch.ElapsedMilliseconds} ms");

            // 5. Validate all incoming orders concurrently
            var validationTasks = incomingOrders
                .Select(order =>
                    _orderValidator.ValidateOrderAsync(order, ct))
                .ToList();

            await Task.WhenAll(validationTasks);

            // 6. Get the validated orders from orders.json
            var savedOrders =
                await _jsonFileStore.ReadAllAsync<Order>(
                    DataFiles.Orders, ct);

            var ordersToProcess = savedOrders
                .Where(order => order.Status == OrderStatus.Validated)
                .ToList();

            // 7. Start processing all eligible orders concurrently
            var processingWatch = Stopwatch.StartNew();

            List<Task<bool>> processingTasks = ordersToProcess
                .Select(order =>
                    _orderProcessorV2.ProcessValidatedOrderAsync(
                        order, ct))
                .ToList();

            if (processingTasks.Count > 0)
            {
                Console.WriteLine(
                    $"First task status: {processingTasks[0].Status}");
            }

            // 8. Observe task statuses after one second
            await Task.Delay(1000, ct);

            foreach (var task in processingTasks)
            {
                Console.WriteLine(
                    $"Task status after 1 second: {task.Status}");
            }

            // 9. Await each task and handle failures individually
            for (int i = 0; i < processingTasks.Count; i++)
            {
                var task = processingTasks[i];
                var order = ordersToProcess[i];

                try
                {
                    bool succeeded = await task;

                    Logging.Info(
                        $"Order #{order.Id}: Task Status = {task.Status}, " +
                        $"Success = {succeeded}");
                }
                catch (OperationCanceledException)
                    when (ct.IsCancellationRequested)
                {
                    // Cancellation is not an ordinary order failure.
                    throw;
                }
                catch (Exception ex)
                {
                    Logging.Error(
                        $"Order #{order.Id} failed: {ex.Message}");

                    // The processor reserves all items before pricing.
                    // A failure here means the reservation phase completed.
                    foreach (var item in order.Items)
                    {
                        try
                        {
                            await _productRepository.ReleaseStockAsync(
                                item.ProductId, item.Quantity, ct);
                        }
                        catch (Exception releaseEx)
                        {
                            Logging.Error(
                                $"Could not release stock for order " +
                                $"#{order.Id}, product {item.ProductId}: " +
                                releaseEx.Message);
                        }
                    }

                    // Save the failed order
                    order.Status = OrderStatus.Failed;
                    order.FailureReason = ex.Message;

                    await _jsonFileStore.UpdateAllAsync<Order>(
                        DataFiles.Orders,
                        orders =>
                        {
                            int index = orders.FindIndex(
                                o => o.Id == order.Id);

                            if (index >= 0)
                            {
                                orders[index] = order;
                            }
                        },
                        ct);
                }
            }

            // 10. Print final task statuses and total processing time
            processingWatch.Stop();

            Console.WriteLine("\n--- Final Task Statuses ---");

            for (int i = 0; i < processingTasks.Count; i++)
            {
                Console.WriteLine(
                    $"Order #{ordersToProcess[i].Id}: " +
                    $"{processingTasks[i].Status}");
            }

            Logging.Info(
                $"Total processing time: " +
                $"{processingWatch.ElapsedMilliseconds} ms");

            // 11. Verify the saved order count and final results
            var finalOrders =
                await _jsonFileStore.ReadAllAsync<Order>(
                    DataFiles.Orders, ct);

            Console.WriteLine(
                $"\nTotal saved orders: {finalOrders.Count}");

            Console.WriteLine(
                $"Completed: {finalOrders.Count(o => o.Status == OrderStatus.Completed)}");

            Console.WriteLine(
                $"Rejected: {finalOrders.Count(o => o.Status == OrderStatus.Rejected)}");

            Console.WriteLine(
                $"Failed: {finalOrders.Count(o => o.Status == OrderStatus.Failed)}");

            Console.WriteLine(
                $"PaymentFailed: {finalOrders.Count(o => o.Status == OrderStatus.PaymentFailed)}");

            if (finalOrders.Count != 20)
            {
                Logging.Error(
                    $"Expected 20 orders, but found {finalOrders.Count}.");
            }
            else
            {
                Logging.Success(
                    "Verified: orders.json contains exactly 20 orders.");
            }
        }
    }

}
