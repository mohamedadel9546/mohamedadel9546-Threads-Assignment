using System.Collections.Concurrent;
using System.Diagnostics;
using Json.Models;
using Json.Repositories;
using Json.Shared;
using Json.Shared.Exceptions_;

namespace Json.Services;

public sealed record ShippingQuote(string Carrier, decimal Price, long ElapsedMs, IReadOnlyList<string>? CancelledCarriers = null);
public interface IShippingCarrier
{
    string Name { get; }
    Task<decimal> GetQuoteAsync(Order order, CancellationToken ct);
}

public sealed class ShippingCarrier : IShippingCarrier
{
    private readonly JsonFileStore _store;
    private readonly decimal _factor;
    private readonly int _minMs, _maxMs;
    private readonly HashSet<int> _failForOrderIds;
    private readonly bool _failsOnEvenOrders;
    public string Name { get; }

    public ShippingCarrier(string name, JsonFileStore store, decimal factor, int minMs, int maxMs,
        IEnumerable<int>? failForOrderIds = null, bool failsOnEvenOrders = false)
    { Name = name; _store = store; _factor = factor; _minMs = minMs; _maxMs = maxMs; _failForOrderIds = failForOrderIds?.ToHashSet() ?? new(); _failsOnEvenOrders = failsOnEvenOrders; }

    public async Task<decimal> GetQuoteAsync(Order order, CancellationToken ct)
    {
        try
        {
            await Task.Delay(150, ct);
            await Task.Delay(Random.Shared.Next(_minMs, _maxMs + 1), ct);
            if (_failForOrderIds.Contains(order.Id) || (_failsOnEvenOrders && order.Id % 2 == 0))
                throw new ShippingProviderUnavailableException($"{Name} unavailable for order #{order.Id}");
            var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct);
            foreach (var item in order.Items)
                if (!products.Any(p => p.Id == item.ProductId))
                    throw new InvalidOperationException($"Product #{item.ProductId} not found");
            decimal subtotal = 0m;
            decimal weight = 0m;
            foreach (var item in order.Items)
            {
                var product = products.First(p => p.Id == item.ProductId);
                subtotal += product.Price * item.Quantity;
                weight += product.WeightKg * item.Quantity;
            }
            int qty = order.Items.Sum(i => i.Quantity);
            decimal basePrice = subtotal >= 1000m ? 0m : 5m + 1.5m * qty + 2m * weight;
            return Math.Round(basePrice * _factor, 2, MidpointRounding.AwayFromZero);
        }
        catch (OperationCanceledException)
        {
            Logging.Info($"[{Name}] cancelled");
            throw;
        }
    }
}

public sealed class ShippingQuoteService
{
    private readonly IReadOnlyList<IShippingCarrier> _carriers;
    public ShippingQuoteService(IEnumerable<IShippingCarrier> carriers) => _carriers = carriers.ToList();

    public async Task<ShippingQuote> GetFastestQuoteAsync(Order order, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var budgetCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budgetCts.Token);
        var taskCarriers = _carriers.ToDictionary(c => c, c => RunCarrierAsync(c, order, linked.Token, sw));
        var running = taskCarriers.Values.ToList();
        var carrierByTask = taskCarriers.ToDictionary(pair => pair.Value, pair => pair.Key);
        var pending = new HashSet<Task<(IShippingCarrier Carrier, decimal? Price, Exception? Error)>>(running);
        var errors = new List<Exception>();
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            var result = await completed;
            if (result.Error is not null) errors.Add(result.Error);
            if (result.Price.HasValue)
            {
                var cancelledCarriers = pending.Select(task => carrierByTask[task].Name).ToList();
                linked.Cancel();
                _ = ObserveLosersAsync(pending);
                sw.Stop();
                return new ShippingQuote(result.Carrier.Name, result.Price.Value, sw.ElapsedMilliseconds, cancelledCarriers);
            }
            if (budgetCts.IsCancellationRequested)
            {
                var cancelledCarriers = pending.Select(task => carrierByTask[task].Name).ToList();
                linked.Cancel();
                _ = ObserveLosersAsync(pending);
                sw.Stop();
                return new ShippingQuote("Flat rate", 25.00m, sw.ElapsedMilliseconds, cancelledCarriers);
            }
            if (pending.Count == 0 && errors.Count(e => e is not OperationCanceledException) == _carriers.Count)
                throw new ShippingProviderUnavailableException("All carriers failed");
        }
        if (errors.Count(e => e is not OperationCanceledException) == _carriers.Count) throw new ShippingProviderUnavailableException("All carriers failed");
        return new ShippingQuote("Flat rate", 25m, sw.ElapsedMilliseconds);
    }

    private static async Task<(IShippingCarrier Carrier, decimal? Price, Exception? Error)> RunCarrierAsync(IShippingCarrier carrier, Order order, CancellationToken ct, Stopwatch sw)
    {
        try { var price = await carrier.GetQuoteAsync(order, ct); return (carrier, price, null); }
        catch (OperationCanceledException ex) { return (carrier, null, ex); }
        catch (Exception ex) { Logging.Error($"[{carrier.Name}] failed: {ex.Message}"); return (carrier, null, ex); }
    }

    private static async Task ObserveLosersAsync(IEnumerable<Task<(IShippingCarrier Carrier, decimal? Price, Exception? Error)>> tasks)
    { try { await Task.WhenAll(tasks); } catch { /* all exceptions are observed by RunCarrierAsync */ } }
}
