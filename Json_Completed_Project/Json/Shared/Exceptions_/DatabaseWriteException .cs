using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Shared.Exceptions_
{
    public class DatabaseWriteException : Exception
    {
        public DatabaseWriteException(string message)
            : base(message)
        {
        }
    }
}
