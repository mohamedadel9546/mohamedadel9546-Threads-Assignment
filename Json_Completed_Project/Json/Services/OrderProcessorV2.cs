using Json.Models;
using Json.Repositories;
using Json.Shared;
using Json.Shared.Exceptions_;
using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Services
{
    public class OrderProcessorV2
    {
        private readonly JsonFileStore jsonFileStore;
        private readonly ProductRepository productRepository;
        private readonly OrderPricer _orderPricer;
        private readonly InvoiceGeneratorService _invoiceGenerator;
        private readonly CustomerService _customerService;
        private readonly OrderRepository _orderRepository;
        private readonly InventoryService _inventoryService;
        private readonly PricingService _pricingService;
        private readonly IPaymentGateway _paymentGateway;
        private readonly IEmailService _emailService;
        public OrderProcessorV2(JsonFileStore jsonFileStore, ProductRepository productRepository, OrderPricer orderPricer,
            InvoiceGeneratorService invoiceGenerator, CustomerService customerService, OrderRepository orderRepository,
            InventoryService inventoryService, PricingService pricingService,
           IPaymentGateway paymentGateway, IEmailService emailService)
        {
            this.jsonFileStore = jsonFileStore;
            this.productRepository = productRepository;
            _orderPricer = orderPricer;
            _invoiceGenerator = invoiceGenerator;
            _customerService = customerService;
             _orderRepository = orderRepository;
             _inventoryService = inventoryService;
             _pricingService = pricingService;
             _paymentGateway = paymentGateway;
             _emailService = emailService;
        }



        public async Task<bool> ProcessOrderAsync(Order order, CancellationToken ct)
        {
            Customer? customer = null;
            bool stockReserved = false;
            string? capturedTransactionId = null;
            var totalWatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var stepWatch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    customer = await _customerService.GetCustomerAsync(order.CustomerId, ct);
                }
                catch (CustomerNotFoundException ex)
                {
                    order.Status = OrderStatus.Rejected;
                    order.FailureReason = ex.Message;
                    await _orderRepository.SaveAsync(order, ct);
                    Logging.Info($"Order #{order.Id} -> Step 1/6 Customer REJECTED in {stepWatch.ElapsedMilliseconds} ms");
                    return false;
                }
                Logging.Success($"Order #{order.Id} -> Step 1/6 Customer OK in {stepWatch.ElapsedMilliseconds} ms");

                if (!customer.IsActive)
                {
                    order.Status = OrderStatus.Rejected;
                    order.FailureReason = "Customer is inactive";
                    await _orderRepository.SaveAsync(order, ct);
                    Logging.Error($"Order #{order.Id} -> Step 1/6 Customer INACTIVE in {stepWatch.ElapsedMilliseconds} ms");
                    return false;
                }

                stepWatch.Restart();
                stockReserved = await _inventoryService.ReserveStockAsync(order, ct);
                if (!stockReserved)
                {
                    var products = await jsonFileStore.ReadAllAsync<Product>(DataFiles.Products, ct);
                    string? unavailableName = null;
                    foreach (var item in order.Items)
                    {
                        var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                        if (product is null || product.StockQuantity < item.Quantity)
                        {
                            unavailableName = product?.Name ?? $"Product {item.ProductId}";
                            break;
                        }
                    }
                    order.Status = OrderStatus.Rejected;
                    order.FailureReason = unavailableName is null
                        ? "Insufficient stock for one or more products"
                        : $"Insufficient stock for '{unavailableName}'";
                    await _orderRepository.SaveAsync(order, ct);
                    Logging.Error($"Order #{order.Id} -> Step 2/6 Stock REJECTED in {stepWatch.ElapsedMilliseconds} ms");
                    return false;
                }
                Logging.Success($"Order #{order.Id} -> Step 2/6 Stock reserved in {stepWatch.ElapsedMilliseconds} ms");

                stepWatch.Restart();
                try
                {
                    await _pricingService.CalculateTotalAsync(order, ct);
                }
                catch (InvalidCouponException ex)
                {
                    await _inventoryService.ReleaseStockAsync(order, CancellationToken.None);
                    stockReserved = false;
                    order.Status = OrderStatus.Rejected;
                    order.FailureReason = ex.Message;
                    await _orderRepository.SaveAsync(order, ct);
                    Logging.Error($"Order #{order.Id} -> Step 3/6 Pricing REJECTED in {stepWatch.ElapsedMilliseconds} ms");
                    return false;
                }
                Logging.Success($"Order #{order.Id} -> Step 3/6 Pricing OK in {stepWatch.ElapsedMilliseconds} ms");

                stepWatch.Restart();
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                PaymentResult paymentResult;
                try
                {
                    paymentResult = await _paymentGateway.ProcessPaymentAsync(order.Id, order.TotalAmount, linkedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    await _inventoryService.ReleaseStockAsync(order, CancellationToken.None);
                    stockReserved = false;
                    order.Status = OrderStatus.PaymentFailed;
                    order.FailureReason = "Payment gateway timeout (3s)";
                    await _orderRepository.SaveAsync(order, ct);
                    Logging.Error($"Order #{order.Id} -> Step 4/6 Payment TIMEOUT after {stepWatch.ElapsedMilliseconds} ms, released");
                    return false;
                }

                if (!paymentResult.Success)
                {
                    await _inventoryService.ReleaseStockAsync(order, CancellationToken.None);
                    stockReserved = false;
                    order.Status = OrderStatus.PaymentFailed;
                    order.FailureReason = "Card declined by issuer";
                    await _orderRepository.SaveAsync(order, ct);
                    Logging.Error($"Order #{order.Id} -> Step 4/6 Payment DECLINED after {stepWatch.ElapsedMilliseconds} ms, released");
                    return false;
                }

                capturedTransactionId = paymentResult.TransactionId;
                order.TransactionId = paymentResult.TransactionId;
                order.Status = OrderStatus.Completed;
                order.FailureReason = null;

                stepWatch.Restart();
                try
                {
                    await _orderRepository.SaveAsync(order, ct);
                }
                catch (DatabaseWriteException)
                {
                    if (!string.IsNullOrWhiteSpace(order.TransactionId))
                        await _paymentGateway.RefundAsync(order.TransactionId);
                    capturedTransactionId = null;
                    await _inventoryService.ReleaseStockAsync(order, CancellationToken.None);
                    stockReserved = false;
                    order.Status = OrderStatus.Failed;
                    order.FailureReason = $"Database save failed after payment; payment {order.TransactionId} refunded";
                    await _orderRepository.SaveAsync(order, CancellationToken.None);
                    Logging.Error($"Order #{order.Id} -> Step 5/6 Save FAILED; refunded in {stepWatch.ElapsedMilliseconds} ms");
                    return false;
                }
                Logging.Success($"Order #{order.Id} -> Step 5/6 Save OK in {stepWatch.ElapsedMilliseconds} ms");

                stepWatch.Restart();
                await _emailService.SendConfirmationAsync(order, customer!, ct);
                Logging.Success($"Order #{order.Id} -> Step 6/6 Email workflow finished in {stepWatch.ElapsedMilliseconds} ms");
                totalWatch.Stop();
                Logging.Info($"Order #{order.Id} -> workflow finished as {order.Status} in {totalWatch.ElapsedMilliseconds} ms");
                return order.Status == OrderStatus.Completed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // A run cancellation is not a payment failure. Restore side effects using a non-cancelled token.
                if (!string.IsNullOrWhiteSpace(capturedTransactionId))
                {
                    try { await _paymentGateway.RefundAsync(capturedTransactionId); }
                    catch (Exception ex) { Logging.Error($"Order #{order.Id}: refund during cancellation failed: {ex.Message}"); }
                }
                if (stockReserved)
                {
                    try { await _inventoryService.ReleaseStockAsync(order, CancellationToken.None); }
                    catch (Exception ex) { Logging.Error($"Order #{order.Id}: stock release during cancellation failed: {ex.Message}"); }
                }
                order.Status = OrderStatus.Pending;
                order.FailureReason = "Cancelled";
                try { await _orderRepository.SaveAsync(order, CancellationToken.None); }
                catch (Exception ex) { Logging.Error($"Order #{order.Id}: could not save cancellation state: {ex.Message}"); }
                throw;
            }
        }

        public async Task<bool> ProcessValidatedOrderAsync(
        Order order,
        CancellationToken token)
        {
            var reservedItems = new List<OrderItem>();

            foreach (var item in order.Items)
            {
                bool reserved = await productRepository
                    .ReserveStockAsync(
                        item.ProductId,
                        item.Quantity,
                        token);

                if (!reserved)
                {
                    foreach (var reservedItem in reservedItems)
                    {
                        await productRepository.ReleaseStockAsync(
                            reservedItem.ProductId,
                            reservedItem.Quantity,
                            token);
                    }

                    var products = await jsonFileStore.ReadAllAsync<Product>(
                        DataFiles.Products, token);

                    var product = products.FirstOrDefault(
                        p => p.Id == item.ProductId);

                    string productName = product?.Name
                        ?? $"Product {item.ProductId}";

                    order.Status = OrderStatus.Rejected;
                    order.FailureReason =
                        $"Insufficient stock for '{productName}'";

                    await jsonFileStore.UpdateAllAsync<Order>(
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
                        token);

                    return false;
                }

                reservedItems.Add(item);
            }

            await _orderPricer.PriceOrderAsync(order, token);
            await _invoiceGenerator.GenerateAsync(order, token);


            order.Status = OrderStatus.Completed;
            order.FailureReason = null;

            await jsonFileStore.UpdateAllAsync<Order>(
                DataFiles.Orders,
                orders =>
                {
                    int index = orders.FindIndex(o => o.Id == order.Id);

                    if (index >= 0)
                    {
                        orders[index] = order;
                    }
                },
                token);
 
            return true;
        }

    }
}
