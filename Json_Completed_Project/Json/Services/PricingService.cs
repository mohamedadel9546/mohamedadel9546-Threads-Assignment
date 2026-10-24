using Json.Models;
using Json.Repositories;
using Json.Shared;
using Json.Shared.Exceptions_;
using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Services
{
    public class PricingService
    {
        private readonly ProductRepository _productRepository;
        private readonly CustomerRepository _customerRepository;
        private readonly CouponRepository _couponRepository;
        private readonly HashSet<int> _failShippingForOrderIds;

        public PricingService(
            ProductRepository productRepository,
            CustomerRepository customerRepository,
            CouponRepository couponRepository,
            IEnumerable<int> failShippingForOrderIds)
        {
            _productRepository = productRepository;
            _customerRepository = customerRepository;
            _couponRepository = couponRepository;

            _failShippingForOrderIds = new HashSet<int>(
                failShippingForOrderIds);
        }

        public async Task<decimal> CalculateSubTotalAsync(Order order, CancellationToken ct)
        {
            decimal subtotal = 0m;

            foreach (var item in order.Items)
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId, ct);

                if (product is null)
                {
                    throw new InvalidOperationException(
                    $"Product #{item.ProductId} not found.");
                }

                item.UnitPrice = product.Price;

                decimal itemtotal = item.UnitPrice * item.Quantity;
                subtotal += itemtotal;
            }
            subtotal = Math.Round(subtotal, 2, MidpointRounding.AwayFromZero);

            Logging.Success(
       $"PricingService -> Order #{order.Id} SubTotal: {subtotal:F2}");

            return subtotal;
        }

        public async Task<decimal> GetDiscountAsync(Order order, CancellationToken ct)
        {
            var customer = await _customerRepository.GetByIdAsync(order.CustomerId, ct);
            if (customer is null)
            {
                throw new InvalidOperationException(
           $"Customer #{order.CustomerId} not found.");
            }

            decimal loyaltyPercent = customer.LoyaltyTier switch
            {
                LoyaltyTier.Bronze => 0m,
                LoyaltyTier.Silver => 5m,
                LoyaltyTier.Gold => 10m,
                _ => 0m
            };

            decimal couponPercent = 0m;
            if (!string.IsNullOrWhiteSpace(order.CouponCode))
            {
                var coupon = await _couponRepository.GetByCodeAsync(order.CouponCode,ct);

                if (coupon is null)
                    throw new InvalidCouponException($"Coupon '{order.CouponCode}' was not found.");
                if (coupon.ExpiryDate.Date < DateTime.Today)
                    throw new InvalidCouponException($"Coupon '{order.CouponCode}' has expired");
                couponPercent = coupon.DiscountPercent;
             
            }

            decimal TotaldiscountPrecent = Math.Min(loyaltyPercent + couponPercent, 30);

            decimal discount = order.SubTotal * TotaldiscountPrecent / 100;
            Logging.Success(
      $"PricingService -> Order #{order.Id} Discount: {discount:F2} ({TotaldiscountPrecent}%)");

            return discount;
        }

        public async Task<decimal> CalculateShippingCostAsync(Order order, CancellationToken ct)
        {
            await SimulatedDelay.WaitAsync(
             $"Shipping API -> Order #{order.Id}: DNS/handshake",
                150, 150, ct);

            if (_failShippingForOrderIds.Contains(order.Id))
            {
                throw new InvalidOperationException(
          "Shipping provider unavailable");
            }

            await SimulatedDelay.WaitAsync(
      $"Shipping API -> Order #{order.Id}: Sending request",
      300, 300, ct);

            // Simulate waiting for the provider's response.
            await SimulatedDelay.WaitAsync(
                $"Shipping API -> Order #{order.Id}: Waiting for response",
                500, 1500, ct);

            if (order.SubTotal >= 1000) {
                Logging.Info(
           $"Shipping API -> Order #{order.Id}: Free shipping.");
                return 0m;
            }

            decimal totalquinty = 0m;
            decimal totalweigh = 0m;
            foreach(var item in order.Items)
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId,ct);
                if(product is null)
                {
                    throw new InvalidOperationException(
               $"Product #{item.ProductId} not found.");
                }
                totalquinty += item.Quantity;
                totalweigh += product.WeightKg * item.Quantity;
            }

            decimal shiping = 5 + (1.5m * totalquinty) + (2m * totalweigh);

            shiping = Math.Round(shiping, 2, MidpointRounding.AwayFromZero);

            Logging.Success(
    $"Shipping API -> Order #{order.Id}: Shipping cost {shiping:F2}");

            return shiping;
        }


        public async Task<decimal> CalculateTotalAsync(Order order, CancellationToken ct)
        {
            order.SubTotal = await CalculateSubTotalAsync(order, ct);

            Task<decimal> discountTask = GetDiscountAsync(order, ct);
            Task<decimal> shippingTask = CalculateShippingCostAsync(order, ct);

            await Task.WhenAll(discountTask, shippingTask);

            order.Discount = await discountTask;
            order.ShippingCost = await shippingTask;

            order.TotalAmount = Math.Round(order.SubTotal - order.Discount + order.ShippingCost, 2, MidpointRounding.AwayFromZero);
            Logging.Success(
           $"OrderPricer -> Order #{order.Id} priced. " +
           $"SubTotal: {order.SubTotal:F2}, " +
           $"Discount: {order.Discount:F2}, " +
           $"Shipping: {order.ShippingCost:F2}, " +
           $"Total: {order.TotalAmount:F2}");
            return order.TotalAmount;
        }

    }
}
