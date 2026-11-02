using Json.Models;
using Json.Repositories;
using Json.Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Services
{
    public class ReportGenerator
    {
        private readonly JsonFileStore jsonFileStore;
        private readonly OrderRepository orderRepository;
        private readonly ProductRepository productRepository;

        public ReportGenerator(JsonFileStore jsonFileStore, OrderRepository orderRepository, ProductRepository productRepository)
        {
            this.jsonFileStore = jsonFileStore;
            this.orderRepository = orderRepository;
            this.productRepository = productRepository;
        }


        public async Task<Report> GenerateAsync(CancellationToken ct)
        {
            var orders = await orderRepository.GetAllAsync(ct);

            var completedorder = orders
                .Where(order => order.Status == Models.OrderStatus.Completed)
                .ToList();

            var Report = new Report()
            {
                TotalOrders = orders.Count,
                RejectedCount = orders.Count(order => order.Status == OrderStatus.Rejected),
                CompletedCount = orders.Count(order => order.Status == OrderStatus.Completed),
                FailedCount = orders.Count(order => order.Status == OrderStatus.Failed),
                PaymentFailedCount = orders.Count(order => order.Status == OrderStatus.PaymentFailed),

                TotalRevenue = completedorder.Sum(c => c.TotalAmount),

                AverageOrderValue = completedorder.Count == 0 ? 0 : completedorder.Sum(c => c.TotalAmount) / completedorder.Count
            };
            var productSales = completedorder
              .SelectMany(o => o.Items)
              .GroupBy(item => item.ProductId)
              .Select(group => new
              {
                  ProductId = group.Key,
                  Quantity = group.Sum(item => item.Quantity)
              })
              .OrderByDescending(x => x.Quantity)
              .ThenBy(x => x.ProductId)
              .FirstOrDefault();

            if (productSales is not null)
            {
                var product = await productRepository.GetByIdAsync(
                    productSales.ProductId, ct);

                Report.TopSellingProduct = product?.Name
                    ?? $"Product {productSales.ProductId}";

                Report.TopSellingQuantity = productSales.Quantity;
            }


            string reportsDirectory = Path.Combine(
                  AppContext.BaseDirectory,
                  "Data",
                  "reports");

            Directory.CreateDirectory(reportsDirectory);

            string fileName =
                $"reports/report_{DateTime.Now:yyyyMMdd_HHmmss}.json";

            await jsonFileStore.WriteAllAsync(
                fileName,
                new List<Report> { Report },
                ct);

            Logging.Success("Report generated and saved successfully.");

            return Report;
        }
    }
}
