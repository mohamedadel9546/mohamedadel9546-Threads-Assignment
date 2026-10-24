using Json.Models;
using Json.Repositories;
using Json.Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Services
{
    public class RaceConditionDemo
    {
        private readonly JsonFileStore _jsonFile;
        private readonly ProductRepository _productRepository;

        public RaceConditionDemo(
            JsonFileStore jsonFile,
            ProductRepository productRepository)
        {
            _jsonFile = jsonFile;
            _productRepository = productRepository;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            // Part 1: Demonstrate the race condition.
            await SetKeyboardStockAsync(1, ct);

            Task<bool> order10Unsafe =
                _productRepository.ReserveStockUnsafeAsync(4, 1, ct);

            Task<bool> order11Unsafe =
                _productRepository.ReserveStockUnsafeAsync(4, 1, ct);

            bool unsafeResult10 = await order10Unsafe;
            bool unsafeResult11 = await order11Unsafe;

            int unsafeStock = await GetKeyboardStockAsync(ct);

            Console.WriteLine("\n--- Unsafe Reservation ---");
            Console.WriteLine($"Order #10: {unsafeResult10}");
            Console.WriteLine($"Order #11: {unsafeResult11}");
            Console.WriteLine($"Final stock: {unsafeStock}");

            // Part 2: Repeat with safe reservations.
            await SetKeyboardStockAsync(1, ct);

            Task<bool> order10Safe =
                _productRepository.ReserveStockAsync(4, 1, ct);

            Task<bool> order11Safe =
                _productRepository.ReserveStockAsync(4, 1, ct);

            bool safeResult10 = await order10Safe;
            bool safeResult11 = await order11Safe;

            int safeStock = await GetKeyboardStockAsync(ct);

            Console.WriteLine("\n--- Safe Reservation ---");
            Console.WriteLine($"Order #10: {safeResult10}");
            Console.WriteLine($"Order #11: {safeResult11}");
            Console.WriteLine($"Final stock: {safeStock}");
        }

        private async Task SetKeyboardStockAsync(
            int quantity,
            CancellationToken ct)
        {
            await _jsonFile.UpdateAllAsync<Product>(
                DataFiles.Products,
                products =>
                {
                    var keyboard = products.FirstOrDefault(p => p.Id == 4);

                    if (keyboard is null)
                    {
                        throw new InvalidOperationException(
                            "Mechanical Keyboard (P4) was not found.");
                    }

                    keyboard.StockQuantity = quantity;
                },
                ct);
        }

        private async Task<int> GetKeyboardStockAsync(
            CancellationToken ct)
        {
            var products = await _jsonFile.ReadAllAsync<Product>(
                DataFiles.Products, ct);

            var keyboard = products.FirstOrDefault(p => p.Id == 4);

            if (keyboard is null)
            {
                throw new InvalidOperationException(
                    "Mechanical Keyboard (P4) was not found.");
            }

            return keyboard.StockQuantity;
        }
    }
}
