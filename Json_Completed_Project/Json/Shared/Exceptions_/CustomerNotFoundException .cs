using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Shared.Exceptions_
{
    public class CustomerNotFoundException:Exception
    {
        public CustomerNotFoundException(string message)
            :base(message)
        {
            
        }
    }
}
