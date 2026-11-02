using Json.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Services
{
    public interface IPaymentGateway
    {
        Task<PaymentResult> ProcessPaymentAsync(
    int orderId,
    decimal amount,
    CancellationToken ct);

        Task RefundAsync(string transactionId);
    }
}
