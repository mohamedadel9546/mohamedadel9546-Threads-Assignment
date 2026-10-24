using Json.Models;
using Json.Shared;

namespace Json.Services;

public class PaymentGateway : IPaymentGateway
{
    private readonly JsonFileStore _jsonFile;
    private readonly HashSet<int> _timeoutOrderIds;
    private readonly HashSet<int> _declineOrderIds;

    public PaymentGateway(JsonFileStore jsonFile,
        IEnumerable<int>? timeoutOrderIds = null,
        IEnumerable<int>? declineOrderIds = null,
        bool enableFailures = true)
    {
        _jsonFile = jsonFile;
        _timeoutOrderIds = enableFailures ? timeoutOrderIds?.ToHashSet() ?? new() : new();
        _declineOrderIds = enableFailures ? declineOrderIds?.ToHashSet() ?? new() : new();
    }

    public async Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, CancellationToken ct)
    {
        await SimulatedDelay.WaitAsync($"PaymentGateway -> Order #{orderId}: Connecting", 500, 500, ct);
        if (_timeoutOrderIds.Contains(orderId)) await Task.Delay(5000, ct);
        await SimulatedDelay.WaitAsync($"PaymentGateway -> Order #{orderId}: Fraud check", 700, 700, ct);

        bool success = !_declineOrderIds.Contains(orderId);
        string transactionId = "TXN-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var transaction = new PaymentTransaction
        {
            OrderId = orderId,
            Amount = amount,
            Status = success ? PaymentStatus.Captured : PaymentStatus.Declined,
            TransactionId = transactionId,
            CreatedAt = DateTime.UtcNow
        };

        // Atomic append: concurrent orders must not overwrite each other's payments.
        await _jsonFile.UpdateAllAsync<PaymentTransaction>(DataFiles.Payments,
            payments => payments.Add(transaction), ct);

        Logging.Info($"PaymentGateway -> Order #{orderId}: {transaction.Status}, Transaction: {transactionId}");
        return new PaymentResult
        {
            Success = success,
            TransactionId = transactionId,
            Status = transaction.Status,
            Message = success ? "Payment captured successfully" : "Card declined by issuer"
        };
    }

    public async Task RefundAsync(string transactionId)
    {
        await _jsonFile.UpdateAllAsync<PaymentTransaction>(DataFiles.Payments, payments =>
        {
            var transaction = payments.FirstOrDefault(p => p.TransactionId == transactionId)
                ?? throw new InvalidOperationException($"Transaction '{transactionId}' was not found.");
            if (transaction.Status != PaymentStatus.Captured)
                throw new InvalidOperationException($"Transaction '{transactionId}' cannot be refunded.");
            transaction.Status = PaymentStatus.Refunded;
        }, CancellationToken.None);
        Logging.Success($"PaymentGateway -> Transaction {transactionId} refunded.");
    }
}
