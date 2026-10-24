# Assignment 03 — Answers

Student name:  Mohamed Adel
Group / ID: 1

1. System.Threading.Monitor is a class in C# used to synchronize multiple threads when accessing shared resources.
 It prevents multiple threads from executing a critical section of code simultaneously when they use the same lock object.

2. The C# lock statement is traditionally compiled into code that uses Monitor.Enter() and Monitor.Exit() inside a try/finally block.

3.Simplicity and Safety: lock automatically releases the lock when execution leaves its block, even if an exception occurs. When using Monitor.Enter() and Monitor.Exit() directly, you must ensure the lock is released, typically using try/finally.

  Control: lock provides a simple way to protect a critical section. Monitor provides additional methods, such as TryEnter(), Wait(), and Pulse(), for more advanced synchronization scenarios.

 Compiler Behavior: With a regular object, lock traditionally uses Monitor internally. Starting with C# 13, lock with System.Threading.Lock uses its specialized locking mechanism instead

4.Monitor.TryEnter(): Used when a thread wants to acquire a lock without waiting indefinitely, optionally specifying a timeout.

Monitor.Wait(): Used when a thread needs to wait for a condition while temporarily releasing the lock.

Monitor.Pulse(): Used to notify one waiting thread that it can attempt to reacquire the lock.



5.
## Lock
=> [
public class Lock
{
    private object _lock = new();
    public int stock = 5;
    public int sold = 0;

    public void lOck()
    {
        lock (_lock)
        {
            if (stock > 0)
            {
                stock--;
                sold++;
            }

        }
    }

}]

## Monitor
=> [
public class monitor
{
    object _lock = new();
    public int stock = 5;
    public int sold = 0;
    public void MonitoR()
    {
        Monitor.Enter(_lock);
        try
        {

            if (stock > 0)
            {
                stock--;
                sold++;
            }
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }
}
]


Links:https://chatgpt.com/c/6ace21ce-5658-83ea-8f86-dfa9fdc517e2
: https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor?view=net-10.0

-
