using Json.Models;
using Json.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Json.Services
{
    public class OrderPricer
    {
        private readonly PricingService pricingService;

        public OrderPricer(PricingService pricingService)
        {
            this.pricingService = pricingService;
        }

        public async Task PriceOrderAsync(Order order, CancellationToken ct)
        {
            order.SubTotal = await pricingService.CalculateSubTotalAsync(order, ct);

            Task<decimal> discountTask =  pricingService.GetDiscountAsync(order, ct);
            Task<decimal> shippingTask =  pricingService.CalculateShippingCostAsync(order, ct);

            await Task.WhenAll(discountTask, shippingTask);

            order.Discount =await  discountTask;
            order.ShippingCost =await  shippingTask;

            order.TotalAmount = Math.Round(order.SubTotal - order.Discount + order.ShippingCost, 2, MidpointRounding.AwayFromZero);
            Logging.Success(
           $"OrderPricer -> Order #{order.Id} priced. " +
           $"SubTotal: {order.SubTotal:F2}, " +
           $"Discount: {order.Discount:F2}, " +
           $"Shipping: {order.ShippingCost:F2}, " +
           $"Total: {order.TotalAmount:F2}");
        }

        public async Task PriceOrderSequentialAsync(Order order, CancellationToken ct)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            order.SubTotal = await pricingService.CalculateSubTotalAsync(order, ct);

           decimal discountTask =await   pricingService.GetDiscountAsync(order, ct);
            decimal shippingTask =await   pricingService.CalculateShippingCostAsync(order, ct);

            order.Discount = discountTask;
            order.ShippingCost = shippingTask;



            order.TotalAmount = Math.Round(order.SubTotal - order.Discount + order.ShippingCost, 2, MidpointRounding.AwayFromZero);
            stopwatch.Stop();

            Logging.Info($"OrderPricer -> Sequential pricing for Order #{order.Id}: " +
            $"{stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
