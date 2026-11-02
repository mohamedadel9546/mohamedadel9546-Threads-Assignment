using Json.Models;
using Json.Shared;
using Json.Shared.Exceptions_;

namespace Json.Services;

public class CustomerService
{
    private readonly JsonFileStore _jsonFileStore;
    public CustomerService(JsonFileStore jsonFileStore) => _jsonFileStore = jsonFileStore;

    public async Task<Customer> GetCustomerAsync(int id, CancellationToken ct)
    {
        await SimulatedDelay.WaitAsync("CustomerService -> Opening connection", 200, 200, ct);
        await SimulatedDelay.WaitAsync("CustomerService -> Executing customer query", 300, 600, ct);
        var customers = await _jsonFileStore.ReadAllAsync<Customer>(DataFiles.Customers, ct);
        var customer = customers.FirstOrDefault(c => c.Id == id);
        if (customer is null) throw new CustomerNotFoundException($"Customer #{id} not found");
        return customer;
    }
}
