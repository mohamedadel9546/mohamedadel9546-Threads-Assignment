using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Shared
{
    public class DatabaseSimulator
    {
        public  static async Task SimulateQueryAsync(string Repositryname,string QueryDescription
            ,CancellationToken cancellation=default)
        {
            await SimulatedDelay.WaitAsync($"{Repositryname} Opening Conection...",200,200, cancellation);
            await SimulatedDelay.WaitAsync($"{Repositryname} Excecute query  =>{QueryDescription}",300,600, cancellation);
        }

    }
}
