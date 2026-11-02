# E-Commerce Order Processing System — File Storage Edition

Target: .NET 10 console application. No external database or payment/email API is used; JSON files and simulated delays represent those systems.

## Build and run

Install the .NET 10 SDK, then verify compilation with `dotnet build Json/Json.csproj`.

```bash
dotnet run -- phase1
dotnet run -- phase2
dotnet run -- phase3
dotnet run -- phase3-sync
dotnet run -- phase4
dotnet run -- fire-and-forget
dotnet run -- all
dotnet run -- phase3 --cancel-after 1500
```

The application writes runtime data under `bin/Debug/net10.0/Data` (specifically `Path.Combine(AppContext.BaseDirectory, "Data")`). `DataSeeder.ResetAsync()` recreates the data set at the beginning of a phase that requires a clean run.

## Project structure and responsibilities

- `Models/DomainModels.cs`: customers, products, coupons, orders, payments, emails, status enums, and reports.
- `Repositories/`: JSON-backed customer, coupon, product, incoming-order, and order persistence operations.
- `Services/CustomerService.cs`: customer lookup and not-found handling.
- `Services/InventoryService.cs`: reserve all order stock or roll back partial reservations.
- `Services/PricingService.cs` and `OrderPricer.cs`: subtotal, loyalty/coupon discount, shipping formula, sequential/concurrent pricing.
- `Services/PaymentGateway.cs`: simulated payment capture/decline/timeout and refund.
- `Services/EmailService.cs`: confirmation email, retries, and retry queue.
- `Services/OrderProcessorV2.cs`: existing order workflows, including Phase 3 checkout and the Phase 2 validated-order path.
- `Services/Phase1Runner.cs`, `Phase2Runner.cs`, `Phase3Runner.cs`, `Phase4Runner.cs`: executable phase demonstrations.
- `Services/CustomerDashboardService.cs`, `ShippingQuoteService.cs`, `Phase4ScaleService.cs`: Phase 4 concurrent loads, carrier selection, fraud scoring, and bounded email resend.
- `Shared/JsonFileStore.cs`: JSON serialization, per-file asynchronous/synchronous locking, and atomic replacement.
- `Seeding/DataSeeder.cs`: clean deterministic test data.
- `Program.cs`: numbered menu, phase modes, Ctrl+C and `--cancel-after` handling.

## Storage and concurrency

Each collection is a JSON array in its own file: customers, products, coupons, incoming orders, orders, payments, emails, and email retry queue. `JsonFileStore` uses a per-file `SemaphoreSlim`, an update-under-lock operation, and temp-file-plus-rename writes. For concurrent append/update operations use `UpdateAllAsync`; a read followed by a separate write can lose another task's update.

## Phase 3 expected end state

- `orders.json`: 20 orders; expected 11 Completed, 6 Rejected, 2 PaymentFailed (#5 timeout and #8 decline), and 1 Failed (#13 refunded after save failure).
- `payments.json`: 13 records (11 captured, one declined, one refunded); no transaction for timed-out order #5.
- `emails.json`: ten messages; order #3 succeeds on attempt 3.
- `email_retry.json`: one message for order #17 after four failed attempts.
- Stock is consumed only by completed orders; one of orders #10/#11 wins the last Mechanical Keyboard.

## Async and concurrency notes

`await` lets an async workflow yield its thread during I/O waits; it does not necessarily reduce the latency of one individual workflow. It allows many workflows to remain in flight without one dedicated OS thread per workflow. A sync design with a dedicated thread per order has high memory and scheduler overhead at 1,000 orders.

`Task.WhenAll` waits for all independent dashboard loads; when multiple tasks fault, inspect the `AggregateException` on the tasks to report every failure. `Task.WhenEach` (or another completion-oriented mechanism) is appropriate when progress must be printed in completion order rather than start order.

For shipping, the quote service starts all carriers together, selects the first successful result, and cancels losers; a shared 1.2-second budget returns a flat rate if no successful quote arrives in time. If all carriers fail before the budget, it throws `ShippingProviderUnavailableException`.

For fraud scoring, the sequential loop and `Parallel.ForEachAsync` must return the same flagged IDs. The parallel version bounds active CPU work to `Environment.ProcessorCount` and observes cancellation. For email resend, `Parallel.ForEachAsync` bounds simultaneous sends to four; this is I/O-bound concurrency, not CPU parallelism.

## Fire-and-forget

The intentionally broken method starts the email task without awaiting it, so its caller can return before email completion, the process can exit while the send is still pending, and failures may not be observed at the call site. Await the task when email completion is part of the operation's contract. Fire-and-forget is only appropriate when work is deliberately detached and has a reliable lifetime/error-reporting mechanism (for example, a durable queue/hosted worker), not for critical console-app work. `async void` is dangerous because callers cannot await it or catch its asynchronous exceptions; event handlers are the standard exception because event delegate signatures often require `void`.

## Comparison report

Run `phase3-sync` for sync-vs-async timings and thread counts. Run `phase4` for dashboard, carrier quotes, fraud scoring, and bounded email resend measurements. Record the actual console output on the machine used for submission; timings depend on hardware and scheduling and are not hard-coded.

## Notes

Failure injection is configured through constructors: order #5 payment timeout, #8 declined payment, #13 failed completed-order save, #3 two SMTP failures, and #17 persistent SMTP failure. The project is deliberately built around the existing repository/service classes rather than a generic repository or dependency-injection container.


## Phase 3 and Phase 4 questions

### Sync versus async (Task 9.6)

1. **Does async make one workflow faster?** Not necessarily. Both versions still perform the same external work and waits. Async primarily avoids blocking a thread while awaiting I/O.
2. **Why does async scale better?** Many workflows can wait concurrently without one dedicated thread per workflow.
3. **What happens with 1,000 synchronous orders?** A thread-per-order design creates large memory, context-switching, and scheduler overhead and can exhaust resources.
4. **Why use async for database, file, and API calls?** These are I/O-bound waits; async allows the runtime to use threads for other work while I/O is pending.

### Fire-and-forget (Task 9.7)

The broken output may look like `Order processing finished (broken method)` before the email success line. If the console process exits while the email task is pending, the email may never finish. An exception in the un-awaited task is not observed at the call site. In the fixed method, the email success/retry-queue message appears before `Order processing finished (fixed method)`. Use `await` when completion is part of the operation's contract. Fire-and-forget is only acceptable when detached work has a managed lifetime and durable error reporting, such as a background queue. `async void` cannot be awaited and its asynchronous exceptions cannot be caught by the caller; event handlers are the usual exception because event signatures require `void`.

### Phase 4 tool choices

1. **10.1:** `Task.WhenEach` reports tasks in completion order. Awaiting tasks in start order can hide completed results behind a slow earlier task.
2. **10.2:** `Task.WhenAll` starts/awaits all four independent loads together. When several tasks fail, inspect `Exception.Flatten().InnerExceptions` on the faulted tasks (or the `WhenAll` task) to report every failure; a plain `await` normally rethrows one exception at the await point.
3. **10.3:** `Task.WhenAny` plus a linked cancellation token chooses the first successful quote and cancels losing carriers. If losers are not cancelled, they continue consuming time/resources and may continue doing unnecessary work after a quote was selected.
4. **10.4 A:** Fraud scoring is CPU-bound; distributing independent calculations across cores can reduce elapsed time. **10.4 B:** Email sending is I/O-bound; its concurrency benefit comes from overlapping waits, not from needing more CPU cores.
5. **Parallel work versus concurrent async work:** parallel CPU work executes calculations simultaneously on multiple cores; concurrent asynchronous work interleaves operations while each waits for I/O, and may use fewer threads than in-flight operations.
6. **Cancellation:** the order processor records `Pending` with reason `Cancelled`, releases reserved stock, and refunds any payment already captured. A payment that times out before capture has no payment record. Cancellation is not represented as `PaymentFailed`. Exact outcomes depend on which step had completed when cancellation arrived.

## Performance table template

Fill this with measured values from your machine after running each mode; do not treat example numbers as measurements.

| Scenario | Elapsed time | Distinct threads | `ThreadPool.ThreadCount` |
|---|---:|---:|---:|
| Phase 1 sync vs async | Record run output | Record run output | Record run output |
| Phase 2 sequential vs concurrent pricing | Record run output | Record run output | Record run output |
| Phase 2 concurrent processing | Record run output | Record run output | Record run output |
| Phase 3 sync (10 threads) vs async (10 tasks) | Printed by `phase3-sync` | Printed by `phase3-sync` | Printed by `phase3-sync` |
| Phase 4 dashboard | Printed by `phase4` | Record if required | Record if required |
| Phase 4 shipping quotes | Printed by `phase4` | Record if required | Record if required |
| Phase 4 fraud scoring sequential vs parallel | Printed by `phase4` | Printed by `phase4` | Record run output |
| Phase 4 email resend | Printed by `phase4` | Record if required | Record run output |

## Previous phase questions

The detailed Phase 1 and Phase 2 explanations are retained in `ReadmePhasees/ReadmePhase1.md` and `ReadmePhasees/ReadmePhase2.md`. Copy actual console screenshots into your submission README or its linked screenshots folder after running the application.


---

# Appendix A — Phase 1 questions and answers

### 1. Why are the sync and async times almost the same when orders are awaited one after another?

Because the orders are processed sequentially, not concurrently. The next order does not start until the current order finishes.

`async/await` does not automatically make the entire operation faster. Its main benefit is that it allows a thread to be released while an asynchronous operation, such as `Task.Delay()` or asynchronous file I/O, is waiting.

In our project, both versions process the orders one by one. Also, the sync and async implementations do not perform exactly the same work, so their execution times are not a fair performance comparison.

### 2. Is the thread ID before an await always the same as after it? Why? Use your own log lines as evidence.

No. The thread ID is not guaranteed to remain the same after an `await`.

When an asynchronous operation is incomplete, the current method can suspend. After the operation completes, its continuation may run on a different thread, depending on the execution environment and synchronization context.

For example, our logs may show:

```text
[10:29:11.030] [Thread 5] IncomingOrderRepository => Open Connection...
[10:29:11.236] [Thread 5] IncomingOrderRepository => Open Connection Completed 200 ms.
```

These are illustrative examples, not actual measurements from my run. I should use real log lines from my own project as evidence.

If the thread IDs are different, this demonstrates that execution continued on a different thread. If they are the same, that does not mean they must always be the same.

### 3. What does the thread do while Task.Delay() is waiting? And while Thread.Sleep() is waiting?

**`Task.Delay()`**

`Task.Delay()` returns a task that completes after the specified time. When awaited, the async method can suspend without blocking the current thread. That thread can return to the thread pool and perform other work.

**`Thread.Sleep()`**

`Thread.Sleep()` blocks the current thread for the specified duration. The thread cannot execute other work during that time, even though the operating system may schedule other threads.

Therefore, `Task.Delay()` is generally preferable for asynchronous waiting, while `Thread.Sleep()` is a blocking operation.

### 4. Why should a repository method return Task<T> instead of T?

A repository method should return `Task<T>` when it performs asynchronous operations, such as reading a file or querying a database asynchronously.

For example:

```csharp
public async Task<Customer?> GetByIdAsync(
    int id,
    CancellationToken ct)
{
    var customers = await _jsonFileStore
        .ReadAllAsync<Customer>(DataFiles.Customers, ct);

    return customers.FirstOrDefault(c => c.Id == id);
}
```

`Task<Customer?>` represents an asynchronous operation that will eventually produce a customer or `null`.

This allows the caller to use `await`, remain responsive while I/O is pending, and propagate cancellation through the call chain.

Returning `Task<T>` does not automatically make CPU-bound work faster. A synchronous method that performs no asynchronous work can usually return `T` directly.

### 5. What is the difference between File.ReadAllText and File.ReadAllTextAsync? Does the async version occupy a thread while the disk is reading?

`File.ReadAllText()` reads a file synchronously. The calling thread waits until the read operation finishes.

`File.ReadAllTextAsync()` reads a file asynchronously and returns a `Task<string>`. When awaited, the caller can suspend without blocking its thread while the read is pending.

For typical asynchronous file I/O, the application does not need to keep a thread blocked for the entire disk-read operation. However, the exact implementation depends on the operating system and runtime; asynchronous file I/O does not guarantee that no thread is used internally at any point.

**In short:**

- `File.ReadAllText()` — synchronous and blocks the calling thread during the read.
- `File.ReadAllTextAsync()` — asynchronous and allows the caller to await the read without blocking its thread while waiting.

---

# Appendix B — Phase 2 questions and answers

## 1. What is the difference between starting a task and awaiting it?

Starting a task means initiating an asynchronous operation. Awaiting a task means waiting for its completion without blocking
the current thread.

```csharp
Task task = DoWorkAsync(); // Starts the operation
await task;                // Waits asynchronously
```

A task can start running before we await it. However, whether the operation starts immediately depends on how the task is created.

## 2. Does every Task create a new thread? Does Task.Delay() use a thread while waiting?

No. A `Task` represents an asynchronous operation, not necessarily a dedicated thread.

- `Task.Run()` typically queues work to a ThreadPool thread.
- Asynchronous I/O operations can wait for I/O completion without occupying a thread while waiting.
- `Task.Delay()` uses a timer to complete the task after the specified time.
-  It does not need to keep a thread blocked during the delay.

This makes asynchronous operations more efficient when waiting for I/O or time-based operations.

## 3. What happens if you call .Result or .Wait() on an unfinished task instead of await? Why is it dangerous?

`.Result` and `.Wait()` block the current thread until the task completes.

This can cause thread starvation or deadlocks, especially in environments with a synchronization context, 
such as UI applications or older ASP.NET applications.

`await` is generally preferred because it waits asynchronously without blocking the current thread.

Example of a blocking demo (keep this as the only place where `.Result` or `.Wait()` is used):

```csharp
static void BlockingDemo()
{
    Task task = Task.Delay(2000);

    Console.WriteLine("Waiting synchronously...");

    task.Wait(); // Blocks the current thread

    Console.WriteLine("Task completed.");
}
```

Calling `.Result` on a `Task<T>` has the same blocking behaviour, while also returning the task's result.

## 4. What happens to an exception thrown inside a task that is never awaited?

If a task faults and nobody awaits it or checks its exception, 
the application may continue running without immediately reporting the failure at the point where it happened.

The exception is stored in the task. If the task is awaited, 
the exception is rethrown at the `await` point. If it is never observed, 
the failure can go unnoticed, and unobserved task exceptions may be reported later through runtime mechanisms.

Therefore, tasks should be awaited or their failures handled explicitly.

## 5. When should you use Task.Run(), and when should you avoid it?

Use `Task.Run()` mainly for CPU-bound work when you want to move that work onto a ThreadPool thread,
for example, a long calculation that would otherwise block the calling thread.

Avoid wrapping naturally asynchronous I/O operations in `Task.Run()` unnecessarily. For example:

```csharp
// Preferred: asynchronous I/O
await File.WriteAllTextAsync(path, content, token);

// Usually unnecessary:
await Task.Run(() =>
    File.WriteAllText(path, content));
```

For CPU-bound work, `Task.Run()` can improve responsiveness, but it does not make the calculation itself require less CPU time.

## 6. What is the difference between Task and Task<T>?

- `Task` represents an asynchronous operation that does not return a result value.
- `Task<T>` represents an asynchronous operation that produces a result of type `T`.

Example:

```csharp
async Task SaveOrderAsync()
{
    await Task.Delay(100);
}

async Task<int> GetOrderCountAsync()
{
    await Task.Delay(100);
    return 20;
}
```

The first operation completes without returning a value. 
The second returns an integer that can be retrieved using `await`.

## 7. Why can a lock block not contain an await?

The C# compiler does not allow `await` inside a `lock` block because `lock` relies on synchronous acquisition and release of a monitor. 
The lock must be released on the same thread that acquired it, but an asynchronous continuation may resume on a different thread.

For asynchronous mutual exclusion, use `SemaphoreSlim` with `WaitAsync()`:

```csharp
await semaphore.WaitAsync(token);

try
{
    await DoWorkAsync(token);
}
finally
{
    semaphore.Release();
}
```

This allows the operation to wait asynchronously while maintaining exclusive access to the protected section.

## 8. Why did the unprotected stock reservation oversell, and how did you prevent it?

The unprotected stock reservation had a race condition. 
Two concurrent operations could read the same stock quantity before either saved its update.

For example, if the stock was `1`, both operations could read `1`, 
both decide that the reservation was valid, and both write back `0`.
Both reservations would appear successful even though only one unit existed. This is a lost-update problem.

I prevented this by using `UpdateAllAsync<Product>` to perform the stock check and update as one protected read-modify-write operation.
The file-level `SemaphoreSlim` ensures that concurrent updates to the same file are serialized.

As a result, only one order can reserve the last available unit. The other reservation sees that the stock is `0` and fails.
