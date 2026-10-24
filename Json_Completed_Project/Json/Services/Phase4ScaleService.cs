using System.Collections.Concurrent;
using System.Diagnostics;
using Json.Models;
using Json.Shared;

namespace Json.Services;

public sealed class Phase4ScaleService
{
    private readonly JsonFileStore _store;
    private readonly IEmailService _emailService;
    public Phase4ScaleService(JsonFileStore store, IEmailService emailService) { _store = store; _emailService = emailService; }

    public static int Score(int orderId)
    {
        long h = orderId;
        for (int i = 0; i < 100_000; i++) h = (h * 31 + i) % 1_000_000_007;
        return (int)(h % 100);
    }

    public async Task RunFraudScoringAsync(CancellationToken ct)
    {
        const int count = 10_000;
        var sequentialWatch = Stopwatch.StartNew();
        var sequential = new List<int>();
        for (int id = 1; id <= count; id++) { ct.ThrowIfCancellationRequested(); if (Score(id) >= 90) sequential.Add(id); }
        sequentialWatch.Stop();

        var parallelWatch = Stopwatch.StartNew();
        var flagged = new ConcurrentBag<int>();
        var threadIds = new ConcurrentDictionary<int, byte>();
        await Parallel.ForEachAsync(Enumerable.Range(1, count), new ParallelOptions
        { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, (id, token) =>
        {
            token.ThrowIfCancellationRequested();
            threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
            if (Score(id) >= 90) flagged.Add(id);
            return ValueTask.CompletedTask;
        });
        parallelWatch.Stop();
        var parallel = flagged.OrderBy(id => id).ToList();
        Console.WriteLine($"Fraud scoring: sequential flagged={sequential.Count}, {sequentialWatch.ElapsedMilliseconds} ms; parallel flagged={parallel.Count}, {parallelWatch.ElapsedMilliseconds} ms; speed-up={(double)sequentialWatch.ElapsedMilliseconds / Math.Max(1, parallelWatch.ElapsedMilliseconds):F2}x; distinct parallel threads={threadIds.Count}; CPU cores={Environment.ProcessorCount}");
        Console.WriteLine($"Same IDs: {sequential.SequenceEqual(parallel)}; first five: {string.Join(", ", parallel.Take(5))}");
    }

    public async Task RunEmailResendAsync(CancellationToken ct)
    {
        var customers = new List<Customer> { new() { Id = 100, Name = "Test Customer", Email = "test@example.com", IsActive = true } };
        await _store.UpdateAllAsync<Customer>(DataFiles.Customers, list => { if (!list.Any(c => c.Id == 100)) list.AddRange(customers); }, ct);
        var testOrders = Enumerable.Range(1001, 30).Select(id => new Order { Id = id, CustomerId = 100, Status = OrderStatus.Completed }).ToList();
        int current = 0, max = 0;
        var watch = Stopwatch.StartNew();
        await Parallel.ForEachAsync(testOrders, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (order, token) =>
        {
            int active = Interlocked.Increment(ref current);
            int observed;
            do { observed = max; if (active <= observed) break; } while (Interlocked.CompareExchange(ref max, active, observed) != observed);
            try { await _emailService.SendConfirmationAsync(order, customers[0], token); }
            finally { Interlocked.Decrement(ref current); }
        });
        watch.Stop();
        var emails = await _store.ReadAllAsync<EmailMessage>(DataFiles.Emails, ct);
        var count = emails.Count(e => e.OrderId is >= 1001 and <= 1030);
        Console.WriteLine($"Email resend: {count}/30 saved exactly once; max concurrent sends={max}; elapsed={watch.ElapsedMilliseconds} ms");
    }
}
