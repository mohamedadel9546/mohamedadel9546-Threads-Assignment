## Phase 1 — Questions and Answers

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
