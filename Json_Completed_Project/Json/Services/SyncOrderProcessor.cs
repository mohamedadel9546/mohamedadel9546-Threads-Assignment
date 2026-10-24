using Json.Models;
using Json.Shared;

namespace Json.Services;

/// <summary>Synchronous equivalent used only for the Phase 3 performance comparison.</summary>
public sealed class SyncOrderProcessor
{
    private readonly JsonFileStore _store;
    public SyncOrderProcessor(JsonFileStore store) => _store = store;

    private static void Delay(int milliseconds) => Thread.Sleep(milliseconds);

    public void ProcessOrder(Order order)
    {
        // Customer query (sync equivalent of connection + query delays).
        Delay(200); Delay(300);
        var customer = _store.ReadAll<Customer>(DataFiles.Customers).FirstOrDefault(c => c.Id == order.CustomerId);
        if (customer is null)
        {
            order.Status = OrderStatus.Rejected; order.FailureReason = $"Customer #{order.CustomerId} not found"; Save(order); return;
        }
        if (!customer.IsActive)
        {
            order.Status = OrderStatus.Rejected; order.FailureReason = "Customer is inactive"; Save(order); return;
        }

        // Reserve all products; roll back earlier reservations if a later item fails.
        var reserved = new List<OrderItem>();
        foreach (var item in order.Items)
        {
            Delay(200); Delay(100); Delay(400);
            bool ok = false;
            _store.UpdateAll<Product>(DataFiles.Products, products =>
            {
                var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (product is not null && item.Quantity > 0 && product.StockQuantity >= item.Quantity)
                { product.StockQuantity -= item.Quantity; ok = true; }
            });
            Delay(200);
            if (!ok)
            {
                Rollback(reserved);
                var product = _store.ReadAll<Product>(DataFiles.Products).FirstOrDefault(p => p.Id == item.ProductId);
                string productName = product?.Name ?? $"Product {item.ProductId}";
                order.Status = OrderStatus.Rejected;
                order.FailureReason = $"Insufficient stock for '{productName}'";
                Save(order); return;
            }
            reserved.Add(item);
        }

        // Subtotal and product lookups.
        var productsList = _store.ReadAll<Product>(DataFiles.Products);
        order.SubTotal = 0m; decimal weight = 0m; int quantity = 0;
        foreach (var item in order.Items)
        {
            Delay(200); Delay(300);
            var product = productsList.First(p => p.Id == item.ProductId);
            item.UnitPrice = product.Price;
            order.SubTotal += product.Price * item.Quantity;
            weight += product.WeightKg * item.Quantity;
            quantity += item.Quantity;
        }
        order.SubTotal = Math.Round(order.SubTotal, 2, MidpointRounding.AwayFromZero);

        // Loyalty and coupon lookup.
        Delay(200); Delay(300);
        decimal loyalty = customer.LoyaltyTier switch { LoyaltyTier.Silver => 5m, LoyaltyTier.Gold => 10m, _ => 0m };
        decimal couponPercent = 0m;
        if (!string.IsNullOrWhiteSpace(order.CouponCode))
        {
            Delay(200); Delay(300);
            var coupon = _store.ReadAll<Coupon>(DataFiles.Coupons).FirstOrDefault(c => c.Code.Equals(order.CouponCode, StringComparison.OrdinalIgnoreCase));
            if (coupon is null || coupon.ExpiryDate.Date < DateTime.Today)
            {
                Rollback(reserved);
                order.Status = OrderStatus.Rejected;
                order.FailureReason = coupon is null ? $"Coupon '{order.CouponCode}' was not found." : $"Coupon '{order.CouponCode}' has expired";
                Save(order); return;
            }
            couponPercent = coupon.DiscountPercent;
        }
        order.Discount = Math.Round(order.SubTotal * Math.Min(30m, loyalty + couponPercent) / 100m, 2, MidpointRounding.AwayFromZero);

        // Shipping API waits.
        Delay(150); Delay(300); Delay(Random.Shared.Next(500, 1501));
        order.ShippingCost = order.SubTotal >= 1000m ? 0m : 5m + 1.5m * quantity + 2m * weight;
        order.ShippingCost = Math.Round(order.ShippingCost, 2, MidpointRounding.AwayFromZero);
        order.TotalAmount = Math.Round(order.SubTotal - order.Discount + order.ShippingCost, 2, MidpointRounding.AwayFromZero);

        // Payment gateway (failures disabled for the comparison run).
        Delay(500); Delay(700);
        string transactionId = "TXN-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        _store.UpdateAll<PaymentTransaction>(DataFiles.Payments, payments => payments.Add(new PaymentTransaction
        {
            OrderId = order.Id, Amount = order.TotalAmount, Status = PaymentStatus.Captured,
            TransactionId = transactionId, CreatedAt = DateTime.UtcNow
        }));

        order.TransactionId = transactionId;
        order.Status = OrderStatus.Completed;
        order.FailureReason = null;
        Save(order);

        // Email send delay + atomic append.
        Delay(200);
        _store.UpdateAll<EmailMessage>(DataFiles.Emails, emails => emails.Add(new EmailMessage
        {
            Id = Guid.NewGuid(), OrderId = order.Id, To = customer.Email,
            Subject = $"Order #{order.Id} Confirmation", Body = $"Hello {customer.Name}, your order #{order.Id} has been completed successfully.",
            SentAt = DateTime.UtcNow, Attempts = 1
        }));
    }

    private void Rollback(IEnumerable<OrderItem> items)
    {
        foreach (var item in items)
            _store.UpdateAll<Product>(DataFiles.Products, products =>
            {
                var product = products.First(p => p.Id == item.ProductId);
                product.StockQuantity += item.Quantity;
            });
    }

    private void Save(Order order)
    {
        Delay(200); Delay(100); Delay(400);
        _store.UpdateAll<Order>(DataFiles.Orders, orders =>
        {
            int index = orders.FindIndex(o => o.Id == order.Id);
            if (index >= 0) orders[index] = order; else orders.Add(order);
        });
        Delay(200);
    }
}
