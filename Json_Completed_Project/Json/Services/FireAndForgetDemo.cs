using Json.Models;
using Json.Repositories;
using Json.Shared;

namespace Json.Services;

public sealed class FireAndForgetDemo
{
    private readonly OrderRepository _orderRepository;
    private readonly IEmailService _emailService;
    public FireAndForgetDemo(OrderRepository orderRepository, IEmailService emailService) { _orderRepository = orderRepository; _emailService = emailService; }

    public async Task ProcessOrderAsync(Order order, Customer customer)
    {
        await _orderRepository.SaveAsync(order, CancellationToken.None);
#pragma warning disable CS4014
        _emailService.SendConfirmationAsync(order, customer, CancellationToken.None); // intentionally not awaited
#pragma warning restore CS4014
        Console.WriteLine("Order processing finished (broken method)");
    }

    public async Task ProcessFixedAsync(Order order, Customer customer)
    {
        await _orderRepository.SaveAsync(order, CancellationToken.None);
        await _emailService.SendConfirmationAsync(order, customer, CancellationToken.None);
        Console.WriteLine("Order processing finished (fixed method)");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var customer = new Customer { Id = 900, Name = "Demo", Email = "demo@example.com", IsActive = true };
        var order = new Order { Id = 900, CustomerId = 900, Status = OrderStatus.Completed, CreatedAt = DateTime.UtcNow };
        Console.WriteLine("Broken version: completion line can print before email finishes; process exit can terminate the email task.");
        await ProcessOrderAsync(order, customer);
        await Task.Delay(1200, ct); // keep process alive only to observe the demo
        order.Id = 901;
        Console.WriteLine("Fixed version: the method waits for the email before printing completion.");
        await ProcessFixedAsync(order, customer);
    }
}
