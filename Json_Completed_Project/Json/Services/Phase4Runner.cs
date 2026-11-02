using System.Diagnostics;
using Json.Models;
using Json.Repositories;
using Json.Seeding;
using Json.Shared;
using Json.Shared.Exceptions_;

namespace Json.Services;

public sealed class Phase4Runner
{
    private readonly JsonFileStore _store;
    public Phase4Runner(JsonFileStore store) => _store = store;

    private Phase3Runner BuildPhase3() => new(_store, injectFailures: true);

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("\n=== Task 10.1: live checkout progress ===");
        await DataSeeder.ResetAsync(ct);
        var products = new ProductRepository(_store);
        var customers = new CustomerRepository(_store);
        var coupons = new CouponRepository(_store);
        var repo = new OrderRepository(_store, new[] { 13 });
        var customerService = new CustomerService(_store);
        var inventory = new InventoryService(products);
        var pricing = new PricingService(products, customers, coupons, Array.Empty<int>());
        var payment = new PaymentGateway(_store, new[] { 5 }, new[] { 8 });
        var email = new EmailService(_store, new Dictionary<int, int> { [3] = 2, [17] = int.MaxValue });
        var processor = new OrderProcessorV2(_store, products, new OrderPricer(pricing), new InvoiceGeneratorService(_store), customerService, repo, inventory, pricing, payment, email);
        var orders = await _store.ReadAllAsync<Order>(DataFiles.IncomingOrders, ct);
        int done = 0;
        var tasks = orders.Select(async order =>
        {
            var sw = Stopwatch.StartNew();
            try { await processor.ProcessOrderAsync(order, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The processor saved Pending/Cancelled and performed compensation; keep the completion stream alive.
            }
            catch (Exception ex) { Logging.Error($"Order #{order.Id} threw: {ex.Message}"); order.Status = OrderStatus.Failed; order.FailureReason = ex.Message; }
            sw.Stop();
            return (Order: order, ElapsedSeconds: sw.Elapsed.TotalSeconds);
        }).ToList();
        await foreach (var finishedTask in Task.WhenEach(tasks))
        {
            var result = await finishedTask;
            int n = Interlocked.Increment(ref done);
            Console.WriteLine($"[{n,2}/20] Order #{result.Order.Id} -> {result.Order.Status} ({result.Order.FailureReason}) after {result.ElapsedSeconds:F1} s");
        }
        var final = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct);
        foreach (var group in final.GroupBy(o => o.Status).OrderBy(g => g.Key)) Console.WriteLine($"{group.Key}: {group.Count()}");
        ct.ThrowIfCancellationRequested();

        Console.WriteLine("\n=== Task 10.2: customer dashboard ===");
        var dashboardService = new CustomerDashboardService(_store, new[] { 99 });
        try
        {
            var dashboard = await dashboardService.LoadAsync(3, ct);
            Console.WriteLine($"Customer #{dashboard.Customer.Id} {dashboard.Customer.Name} — {dashboard.Orders.Count} orders ({string.Join(", ", dashboard.Orders.OrderBy(o => o.Id).Select(o => $"#{o.Id} {o.Status}"))}), {dashboard.Payments.Count} payments, {dashboard.Emails.Count} emails");
        }
        catch (AggregateException ex) { foreach (var error in ex.Flatten().InnerExceptions) Console.WriteLine(error.Message); }
        try { await dashboardService.LoadAsync(99, ct); }
        catch (AggregateException ex) { foreach (var error in ex.Flatten().InnerExceptions) Console.WriteLine(error.Message); }

        Console.WriteLine("\n=== Task 10.3: fastest shipping quote ===");
        var shippingOrders = await _store.ReadAllAsync<Order>(DataFiles.IncomingOrders, ct);
        foreach (var order in shippingOrders.Where(o => o.Id <= 5))
        {
            var carriers = new IShippingCarrier[]
            {
                new ShippingCarrier("FastShip", _store, 1.00m, 500, 1500),
                new ShippingCarrier("CairoExpress", _store, 0.95m, 300, 2000, failsOnEvenOrders: true),
                new ShippingCarrier("NileCourier", _store, 1.10m, 800, 1200)
            };
            var quote = await new ShippingQuoteService(carriers).GetFastestQuoteAsync(order, ct);
            Console.WriteLine($"Order #{order.Id}: {quote.Carrier}, {quote.Price:F2}, {quote.ElapsedMs} ms; cancelled: {string.Join(", ", quote.CancelledCarriers ?? Array.Empty<string>())}");
        }
        try
        {
            var failingCarriers = new IShippingCarrier[]
            {
                new ShippingCarrier("FastShip", _store, 1m, 100, 200, new[] { 6 }),
                new ShippingCarrier("CairoExpress", _store, .95m, 100, 200, new[] { 6 }),
                new ShippingCarrier("NileCourier", _store, 1.1m, 100, 200, new[] { 6 })
            };
            await new ShippingQuoteService(failingCarriers).GetFastestQuoteAsync(new Order { Id = 6, Items = new() }, ct);
        }
        catch (ShippingProviderUnavailableException ex) { Console.WriteLine($"Order #6 separate all-failed test: {ex.Message}"); }

        Console.WriteLine("\n=== Task 10.4A: fraud scoring ===");
        var scaleEmail = new EmailService(_store, sendDelayMs: 1000);
        var scale = new Phase4ScaleService(_store, scaleEmail);
        await scale.RunFraudScoringAsync(ct);
        Console.WriteLine("\n=== Task 10.4B: bounded email resend ===");
        await scale.RunEmailResendAsync(ct);
    }
}
