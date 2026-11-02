using Json.Models;
using Json.Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Services
{
    public class InvoiceGeneratorService
    {
        private readonly JsonFileStore jsonFileStorel;

        public InvoiceGeneratorService(JsonFileStore jsonFileStorel)
        {
            this.jsonFileStorel = jsonFileStorel;
        }


        public async Task GenerateAsync(Order order, CancellationToken ct)
        {
            long result = await Task.Run(() =>
            {
                long sum = 0;

                for (int i = 0; i < 20_000_000; i++)
                {
                    sum += i;
                }

                return sum;
            }, ct);

            ct.ThrowIfCancellationRequested();

            string invoicePath = Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "invoices");

            Directory.CreateDirectory(invoicePath);

            string orderPath = Path.Combine(
                invoicePath,
                $"invoice_{order.Id}.txt");

            string content = $"""
        Invoice #{order.Id}
        SubTotal: {order.SubTotal:F2}
        Discount: {order.Discount:F2}
        Shipping: {order.ShippingCost:F2}
        Total: {order.TotalAmount:F2}
        """;

            await File.WriteAllTextAsync(orderPath, content, ct);
        }

    }
}
