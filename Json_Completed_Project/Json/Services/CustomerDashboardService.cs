using Json.Models;
using Json.Repositories;
using Json.Shared;

namespace Json.Services;

public sealed class CustomerDashboard
{
    public Customer Customer { get; init; } = new();
    public List<Order> Orders { get; init; } = new();
    public List<PaymentTransaction> Payments { get; init; } = new();
    public List<EmailMessage> Emails { get; init; } = new();
}

public sealed class CustomerDashboardService
{
    private readonly JsonFileStore _store;
    private readonly HashSet<int> _failPaymentsForCustomerIds;
    public CustomerDashboardService(JsonFileStore store, IEnumerable<int>? failPaymentsForCustomerIds = null)
    { _store = store; _failPaymentsForCustomerIds = failPaymentsForCustomerIds?.ToHashSet() ?? new(); }

    public async Task<CustomerDashboard> LoadAsync(int customerId, CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Task<Customer> profile = LoadProfileAsync(customerId, ct);
        Task<List<Order>> orders = LoadOrdersAsync(customerId, ct);
        Task<List<PaymentTransaction>> payments = LoadPaymentsAsync(customerId, ct);
        Task<List<EmailMessage>> emails = LoadEmailsAsync(customerId, ct);
        Task[] all = { profile, orders, payments, emails };
        try { await Task.WhenAll(all); }
        catch
        {
            var errors = all.Where(t => t.IsFaulted).SelectMany(t => t.Exception!.Flatten().InnerExceptions).ToList();
            if (errors.Count == 0) throw;
            throw new AggregateException(errors);
        }
        watch.Stop();
        Logging.Info($"Customer dashboard loads completed in {watch.ElapsedMilliseconds} ms.");
        return new CustomerDashboard { Customer = await profile, Orders = await orders, Payments = await payments, Emails = await emails };
    }

    private async Task<Customer> LoadProfileAsync(int id, CancellationToken ct)
    {
        await SimulatedDelay.WaitAsync("Dashboard profile connection", 200, 200, ct);
        await SimulatedDelay.WaitAsync("Dashboard profile query", 300, 600, ct);
        var customer = (await _store.ReadAllAsync<Customer>(DataFiles.Customers, ct)).FirstOrDefault(c => c.Id == id);
        return customer ?? throw new InvalidOperationException($"Customer #{id} not found");
    }
    private async Task<List<Order>> LoadOrdersAsync(int id, CancellationToken ct)
    {
        await SimulatedDelay.WaitAsync("Dashboard orders connection", 200, 200, ct);
        await SimulatedDelay.WaitAsync("Dashboard orders query", 300, 600, ct);
        return (await _store.ReadAllAsync<Order>(DataFiles.Orders, ct)).Where(o => o.CustomerId == id).ToList();
    }
    private async Task<List<PaymentTransaction>> LoadPaymentsAsync(int id, CancellationToken ct)
    {
        await SimulatedDelay.WaitAsync("Dashboard payments connection", 200, 200, ct);
        await SimulatedDelay.WaitAsync("Dashboard payments query", 300, 600, ct);
        if (_failPaymentsForCustomerIds.Contains(id)) throw new TimeoutException("Payments store timeout");
        var orderIds = (await _store.ReadAllAsync<Order>(DataFiles.Orders, ct)).Where(o => o.CustomerId == id).Select(o => o.Id).ToHashSet();
        return (await _store.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, ct)).Where(p => orderIds.Contains(p.OrderId)).ToList();
    }
    private async Task<List<EmailMessage>> LoadEmailsAsync(int id, CancellationToken ct)
    {
        await SimulatedDelay.WaitAsync("Dashboard emails connection", 200, 200, ct);
        await SimulatedDelay.WaitAsync("Dashboard emails query", 300, 600, ct);
        var orderIds = (await _store.ReadAllAsync<Order>(DataFiles.Orders, ct)).Where(o => o.CustomerId == id).Select(o => o.Id).ToHashSet();
        return (await _store.ReadAllAsync<EmailMessage>(DataFiles.Emails, ct)).Where(e => orderIds.Contains(e.OrderId)).ToList();
    }
}
