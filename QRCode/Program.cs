using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace QRCode
{
    class Program
    {
        public static void Main(string[] args)
        {
            QRCode code = null;
            code = new QRCode("01234567", ErrorCorrection.None);
            // code = new QRCode("THIS IA A LONGER ONE AND WILL HAVE TO BE IN A FULL SYMBOL", ErrorCorrection.None);
            code.Save("Test.png", 4);
        }
    }
}
