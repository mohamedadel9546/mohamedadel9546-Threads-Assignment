using System.Diagnostics;
using Json.Models;
using Json.Repositories;
using Json.Seeding;
using Json.Shared;

namespace Json.Services;

public sealed class Phase3Runner
{
    private readonly JsonFileStore _store;
    private readonly bool _injectFailures;

    public Phase3Runner(JsonFileStore store, bool injectFailures = true)
    {
        _store = store;
        _injectFailures = injectFailures;
    }

    private (OrderProcessorV2 Processor, OrderRepository Orders, ProductRepository Products) BuildProcessor()
    {
        var products = new ProductRepository(_store);
        var customers = new CustomerRepository(_store);
        var coupons = new CouponRepository(_store);
        var orderRepository = new OrderRepository(_store, _injectFailures ? new[] { 13 } : Array.Empty<int>());
        var customerService = new CustomerService(_store);
        var inventory = new InventoryService(products);
        var pricing = new PricingService(products, customers, coupons, Array.Empty<int>());
        var pricer = new OrderPricer(pricing);
        var invoice = new InvoiceGeneratorService(_store);
        var payment = new PaymentGateway(_store,
            timeoutOrderIds: _injectFailures ? new[] { 5 } : Array.Empty<int>(),
            declineOrderIds: _injectFailures ? new[] { 8 } : Array.Empty<int>(),
            enableFailures: _injectFailures);
        var email = new EmailService(_store,
            _injectFailures ? new Dictionary<int, int> { [3] = 2, [17] = int.MaxValue } : new Dictionary<int, int>());
        var processor = new OrderProcessorV2(_store, products, pricer, invoice,
            customerService, orderRepository, inventory, pricing, payment, email);
        return (processor, orderRepository, products);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await DataSeeder.ResetAsync(ct);
        var (processor, _, _) = BuildProcessor();
        var orders = await _store.ReadAllAsync<Order>(DataFiles.IncomingOrders, ct);
        var watch = Stopwatch.StartNew();
        var tasks = orders.Select(o => processor.ProcessOrderAsync(o, ct)).ToList();
        try
        {
            await Task.WhenAll(tasks); // waits for every order task, including cancellation cleanup
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logging.Error($"One or more order tasks failed unexpectedly: {ex.Message}");
        }
        watch.Stop();
        var finalOrders = await _store.ReadAllAsync<Order>(DataFiles.Orders, CancellationToken.None);
        var payments = await _store.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, CancellationToken.None);
        var emails = await _store.ReadAllAsync<EmailMessage>(DataFiles.Emails, CancellationToken.None);
        var retries = await _store.ReadAllAsync<EmailMessage>(DataFiles.EmailRetry, CancellationToken.None);

        Console.WriteLine("\nOrder | Status | Total | Reason");
        foreach (var o in finalOrders.OrderBy(o => o.Id))
            Console.WriteLine($"{o.Id,5} | {o.Status,-13} | {o.TotalAmount,8:F2} | {o.FailureReason}");
        Console.WriteLine($"\nElapsed: {watch.ElapsedMilliseconds} ms");
        Console.WriteLine($"Orders: {finalOrders.Count}; Completed={finalOrders.Count(o => o.Status == OrderStatus.Completed)}, Rejected={finalOrders.Count(o => o.Status == OrderStatus.Rejected)}, PaymentFailed={finalOrders.Count(o => o.Status == OrderStatus.PaymentFailed)}, Failed={finalOrders.Count(o => o.Status == OrderStatus.Failed)}");
        Console.WriteLine($"Payments: {payments.Count} (Captured={payments.Count(p => p.Status == PaymentStatus.Captured)}, Declined={payments.Count(p => p.Status == PaymentStatus.Declined)}, Refunded={payments.Count(p => p.Status == PaymentStatus.Refunded)})");
        Console.WriteLine($"Emails: {emails.Count}; Retry queue: {retries.Count}");
    }

    public async Task RunSyncVsAsyncAsync(CancellationToken ct)
    {
        Console.WriteLine("\n=== Sync vs Async (orders #1-#10, failures disabled) ===");
        await DataSeeder.ResetAsync(ct);
        var input = (await _store.ReadAllAsync<Order>(DataFiles.IncomingOrders, ct)).Where(o => o.Id <= 10).ToList();
        var sync = new SyncOrderProcessor(_store);
        var syncThreads = new List<Thread>();
        var syncWatch = Stopwatch.StartNew();
        foreach (var order in input)
        {
            var copy = order.DeepCopy();
            var thread = new Thread(() => sync.ProcessOrder(copy));
            syncThreads.Add(thread);
            thread.Start();
        }
        foreach (var thread in syncThreads) thread.Join();
        syncWatch.Stop();
        var syncResults = _store.ReadAll<Order>(DataFiles.Orders);
        Console.WriteLine($"SYNC: {syncWatch.ElapsedMilliseconds} ms; distinct dedicated threads={syncThreads.Count}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}; Completed={syncResults.Count(o => o.Status == OrderStatus.Completed)}, Rejected={syncResults.Count(o => o.Status == OrderStatus.Rejected)}");

        await DataSeeder.ResetAsync(ct);
        var (processor, _, _) = BuildProcessor();
        var asyncInput = (await _store.ReadAllAsync<Order>(DataFiles.IncomingOrders, ct)).Where(o => o.Id <= 10).ToList();
        var observedThreads = new System.Collections.Concurrent.ConcurrentDictionary<int, byte>();
        var asyncWatch = Stopwatch.StartNew();
        var tasks = asyncInput.Select(async order =>
        {
            observedThreads.TryAdd(Environment.CurrentManagedThreadId, 0);
            await processor.ProcessOrderAsync(order, ct);
            observedThreads.TryAdd(Environment.CurrentManagedThreadId, 0);
        }).ToList();
        await Task.WhenAll(tasks);
        asyncWatch.Stop();
        var asyncResults = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct);
        Console.WriteLine($"ASYNC: {asyncWatch.ElapsedMilliseconds} ms; observed thread IDs={observedThreads.Count}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}; Completed={asyncResults.Count(o => o.Status == OrderStatus.Completed)}, Rejected={asyncResults.Count(o => o.Status == OrderStatus.Rejected)}");
        Console.WriteLine("README: async does not necessarily make one I/O workflow faster; it releases threads while I/O waits, scales better for many concurrent orders, and avoids creating a dedicated OS thread per order. 1,000 sync threads would consume substantial memory and scheduling overhead.");
    }
}
