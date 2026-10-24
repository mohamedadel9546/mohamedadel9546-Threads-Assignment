# Phase 2 — Questions & Answers

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