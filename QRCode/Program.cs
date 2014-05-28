using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace QRCode
{
    class Program
    {
        public static void Main(string[] args)
        {
            Exception e;
            try
            {
                throw new InvalidOperationException("shit done blowed up");
            }
            catch (Exception ex)
            {
                e = ex;
            }

            var code = new QRCode(e.ToString(), SymbolType.Normal, ErrorCorrection.M);
            code.Save("Test.png", 4);
        }
    }
}
