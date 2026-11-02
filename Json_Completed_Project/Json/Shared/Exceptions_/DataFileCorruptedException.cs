using System;
using System.Collections.Generic;
using System.Text;

namespace Json.Shared
{
    public class DataFileCorruptedException:Exception
    {
        public DataFileCorruptedException(string filename,Exception exception)
            :base($"The json file {filename} is currebted", exception)
        {
            
        }
    }
}
