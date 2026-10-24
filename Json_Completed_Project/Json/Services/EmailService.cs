using Json.Models;
using Json.Shared;
using Json.Shared.Exceptions_;
using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Services
{
    public class EmailService : IEmailService
    {
        private readonly JsonFileStore _jsonFile;
        private readonly Dictionary<int, int> _failAttempts;
        private readonly int _sendDelayMs;

        public EmailService(
            JsonFileStore jsonFile,
            Dictionary<int, int>? failAttempts = null,
            int sendDelayMs = 200,
            bool enableFailures = true)
        {
            _jsonFile = jsonFile;

            _failAttempts = enableFailures ? failAttempts ?? new Dictionary<int, int>() : new Dictionary<int, int>();
            _sendDelayMs = Math.Max(0, sendDelayMs);
        }

        public async Task SendConfirmationAsync(
            Order order,
            Customer customer,
            CancellationToken ct)
        {
            string? lastError = null;

            // Initial attempt + 3 retries = 4 attempts
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    await SimulatedDelay.WaitAsync(
                        $"EmailService -> Sending confirmation for Order #{order.Id}",
                        _sendDelayMs, _sendDelayMs, ct);

                    // Simulate SMTP failures injected for testing.
                    if (_failAttempts.TryGetValue(order.Id, out int failures)
                        && attempt <= failures)
                    {
                        throw new SmtpUnavailableException(
                            "SMTP server is temporarily unavailable.");
                    }

                    var email = new EmailMessage
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        To = customer.Email,
                        Subject = $"Order #{order.Id} Confirmation",
                        Body = $"Hello {customer.Name}, your order #{order.Id} " +
                               $"has been completed successfully.",
                        SentAt = DateTime.UtcNow,
                        Attempts = attempt,
                        LastError = null
                    };

                    await _jsonFile.UpdateAllAsync<EmailMessage>(
                        DataFiles.Emails,
                        emails => emails.Add(email),
                        ct);

                    Logging.Success(
                        $"EmailService -> Order #{order.Id} email sent " +
                        $"successfully on attempt {attempt}.");

                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (SmtpUnavailableException ex)
                {
                    lastError = ex.Message;

                    Logging.Error(
                        $"EmailService -> Order #{order.Id}, " +
                        $"attempt {attempt}/4 failed: {ex.Message}");

                    if (attempt == 4)
                        break;

                    int delayMs = 500 * (1 << (attempt - 1));

                    await Task.Delay(delayMs, ct);
                }
            }

            var failedEmail = new EmailMessage
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                To = customer.Email,
                Subject = $"Order #{order.Id} Confirmation",
                Body = $"Hello {customer.Name}, your order #{order.Id} " +
                       "has been completed successfully.",
                SentAt = DateTime.UtcNow,
                Attempts = 4,
                LastError = lastError
            };

            await _jsonFile.UpdateAllAsync<EmailMessage>(
                DataFiles.EmailRetry,
                emails => emails.Add(failedEmail),
                ct);

            Logging.Error(
                $"EmailService -> Order #{order.Id} queued for retry " +
                "after 4 failed attempts.");
        }
    }
}
