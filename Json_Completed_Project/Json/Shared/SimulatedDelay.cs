using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Shared
{
    public class SimulatedDelay
    {
        public static async Task WaitAsync(string operation,int minmillesceond,int maxmillesecond,
            CancellationToken cancellation=default)
        {
            int delay = Random.Shared.Next(minmillesceond, maxmillesecond + 1);
            Logging.Info($"{operation}...");
            await Task.Delay(delay, cancellation);

            Logging.Success($"{operation} Completed {delay} ms.");
        }

    }
}
