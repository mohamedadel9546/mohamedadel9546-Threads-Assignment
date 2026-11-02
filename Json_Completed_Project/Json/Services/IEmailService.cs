using Json.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Services
{
    public interface IEmailService
    {
   
         Task SendConfirmationAsync(
                Order order,
                Customer customer,
                CancellationToken ct);
     
    }
}
