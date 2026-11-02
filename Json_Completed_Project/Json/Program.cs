using Json.Models;
using Json.Phase1Runners;
using Json.Repositories;
using Json.Seeding;
using Json.Services;
using Json.Shared;

namespace Json;

internal class Program
{
    private static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Logging.Info("Ctrl+C cancellation requested."); cts.Cancel(); };
        int cancelIndex = Array.IndexOf(args, "--cancel-after");
        if (cancelIndex >= 0)
        {
            if (cancelIndex + 1 >= args.Length || !int.TryParse(args[cancelIndex + 1], out int ms) || ms <= 0)
            { Console.WriteLine("Usage: dotnet run -- phase3 --cancel-after <milliseconds>"); return; }
            cts.CancelAfter(ms);
            Logging.Info($"Automatic cancellation scheduled after {ms} ms.");
        }
        string mode = "menu";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--cancel-after") { i++; continue; }
            mode = args[i].ToLowerInvariant();
            break;
        }
        if (mode == "menu")
        {
            Console.WriteLine("1. Phase 1\n2. Phase 2\n3. Phase 3\n4. Phase 4\n5. All phases");
            Console.Write("Choose: ");
            mode = Console.ReadLine() switch { "1" => "phase1", "2" => "phase2", "3" => "phase3", "4" => "phase4", "5" => "all", _ => "phase3" };
        }
        try
        {
            var store = new JsonFileStore();
            if (mode is "phase1" or "all") { await RunPhase1Async(store, cts.Token); await SampleOutputExporter.ExportAsync("phase1"); }
            if (mode is "phase2" or "all") { await RunPhase2Async(store, cts.Token); await SampleOutputExporter.ExportAsync("phase2"); }
            if (mode is "phase3" or "all") { await new Phase3Runner(store).RunAsync(cts.Token); await SampleOutputExporter.ExportAsync("phase3"); }
            if (mode is "phase3-sync" or "all") await new Phase3Runner(store, injectFailures: false).RunSyncVsAsyncAsync(cts.Token);
            if (mode is "phase4" or "all") { await new Phase4Runner(store).RunAsync(cts.Token); await SampleOutputExporter.ExportAsync("phase4"); }
            if (mode is "fire-and-forget") await RunFireAndForgetAsync(store, cts.Token);
            if (mode is not ("phase1" or "phase2" or "phase3" or "phase3-sync" or "phase4" or "all" or "fire-and-forget"))
                Console.WriteLine("Modes: phase1 | phase2 | phase3 | phase3-sync | phase4 | fire-and-forget | all");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Logging.Info("Run cancelled. Cancellation was not recorded as PaymentFailed.");
        }
        catch (Exception ex)
        {
            Logging.Error($"Unhandled error: {ex}");
        }
    }

    private static async Task RunPhase1Async(JsonFileStore store, CancellationToken ct)
    {
        await DataSeeder.ResetAsync(ct);
        var incoming = new IncomingOrderRepository(store);
        var orders = new OrderRepository(store);
        var customers = new CustomerRepository(store);
        var products = new ProductRepository(store);
        var coupons = new CouponRepository(store);
        var validator = new OrderValidator(customers, products, coupons, orders);
        var runner = new Phase1Runner(store, incoming, orders, validator);
        await runner.RunAsync(ct);
    }

    private static async Task RunPhase2Async(JsonFileStore store, CancellationToken ct)
    {
        var products = new ProductRepository(store);
        var customers = new CustomerRepository(store);
        var coupons = new CouponRepository(store);
        var orders = new OrderRepository(store);
        var pricing = new PricingService(products, customers, coupons, new[] { 7 });
        var pricer = new OrderPricer(pricing);
        var validator = new OrderValidator(customers, products, coupons, orders);
        var payment = new PaymentGateway(store);
        var email = new EmailService(store);
        var processor = new OrderProcessorV2(store, products, pricer, new InvoiceGeneratorService(store), new CustomerService(store), orders, new InventoryService(products), pricing, payment, email);
        var runner = new Phase2Runner(new RaceConditionDemo(store, products), pricer, store, validator, processor, products);
        await runner.RunAsync(ct);
    }

    private static async Task RunFireAndForgetAsync(JsonFileStore store, CancellationToken ct)
    {
        await DataSeeder.ResetAsync(ct);
        var repo = new OrderRepository(store);
        var email = new EmailService(store);
        await new FireAndForgetDemo(repo, email).RunAsync(ct);
    }
}
