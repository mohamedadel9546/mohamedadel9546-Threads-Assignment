using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Shared.Exceptions_
{
    public class InvalidCouponException:Exception
    {
        public InvalidCouponException(string message)
            :base(message)
        {
            
        }
    }
}
