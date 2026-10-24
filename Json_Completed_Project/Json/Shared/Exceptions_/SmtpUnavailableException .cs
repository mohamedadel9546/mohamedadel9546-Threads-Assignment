using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Shared.Exceptions_
{
    public class SmtpUnavailableException : Exception
    {
        public SmtpUnavailableException(string message)
            : base(message)
        {
        }
    }
}
