using Json.Models;
using Json.Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Repositories
{
    public class ProductRepository
    {
        private readonly JsonFileStore jsonFile;

        public ProductRepository(JsonFileStore jsonFile)
        {
            this.jsonFile = jsonFile;
        }

        public async Task<Product?> GetByIdAsync(int id,CancellationToken token)
        {
            await SimulatedDelay.WaitAsync(
                "ProductRepository => open Conncetion",
                200,200,
                token
                );

            await SimulatedDelay.WaitAsync(
                "ProductRepository => Execute Query",
                300,600,
                token
                );

            var products =await jsonFile.ReadAllAsync<Product>(DataFiles.Products,token);
            var Result = products.FirstOrDefault(c => c.Id == id);
            if(Result is null)
            {
                Logging.Info($"ProductRepository => Product {id} Not Found");
                return null;
            }
            Logging.Success($"ProductRepository => Product {id}  Found");
            return Result;
        }

        public async Task<bool> ReserveStockAsync(int id,int quinty,CancellationToken token)
        {

            await SimulatedDelay.WaitAsync("ProductRepository Open Connection", 200, 200, token);
            await SimulatedDelay.WaitAsync("ProductRepository Beginning transaction", 100, 100, token);
            await SimulatedDelay.WaitAsync("ProductRepository Updating product stock", 400, 400, token);

            bool Reserved = false;

            await jsonFile.UpdateAllAsync<Product>(DataFiles.Products,
                Products =>
                {
                    var Product = Products.FirstOrDefault(c => c.Id == id);
                    if(Product is null || quinty<=0 || Product .StockQuantity< quinty)
                    {
                        return;
                    }
                    Product.StockQuantity -= quinty;
                    Reserved = true;
                }
                , token);
            if (!Reserved)
            {
                return false;
            }
            try
            {
                await SimulatedDelay.WaitAsync("ProductRepository -> Committing transaction", 200, 200, token);
                return true;
            }
            catch
            {
                // The stock update already happened; compensate if cancellation occurs during commit delay.
                await ReleaseStockAsync(id, quinty, CancellationToken.None);
                throw;
            }
        }

        public async Task ReleaseStockAsync(int id, int quinty, CancellationToken ct)
        {
            await SimulatedDelay.WaitAsync(
       "ProductRepository -> Releasing stock",
       400, 400,
       ct);

            await jsonFile.UpdateAllAsync<Product>(DataFiles.Products,
                 Products =>
                {
                    var product = Products.FirstOrDefault(c=>c.Id==id);
                    if(product is null)
                    {
                        throw new InvalidOperationException(
                    $"Product {id} was not found.");
                    }
                    product.StockQuantity += quinty;
                }
                , ct);
        }


        public async Task<bool> ReserveStockUnsafeAsync(
                int productId, int qty, CancellationToken ct)
        {
            // 1. Read the current product and stock.
            var products = await jsonFile
                .ReadAllAsync<Product>(DataFiles.Products, ct);

            var product = products.FirstOrDefault(
                p => p.Id == productId);

            if (product is null || product.StockQuantity < qty)
                return false;

            // 2. Simulate transaction delays — no protection.
            await SimulatedDelay.WaitAsync(
                "Unsafe reservation: Open connection",
                200, 200, ct);

            await SimulatedDelay.WaitAsync(
                "Unsafe reservation: Begin transaction",
                100, 100, ct);

            await SimulatedDelay.WaitAsync(
                "Unsafe reservation: Update stock",
                400, 400, ct);

            // 3. Update based on the value read earlier.
            product.StockQuantity -= qty;

            // 4. Write without the safe per-file lock.
            await jsonFile.WriteAllAsync(
                DataFiles.Products, products, ct);

            return true;
        }

        public Product? GetById(int id)
        {
            var customers = jsonFile.ReadAll<Product>(DataFiles.Products);
            return customers.FirstOrDefault<Product>(c => c.Id == id);
        }
    }


}
