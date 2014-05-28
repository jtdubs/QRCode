using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace QRCode
{
    class Program
    {
        public static void Main(string[] args)
        {
            var code = new QRCode("THIS IS A TEXTUAL MESSAGE THAT SHOULD ENCODE PROPERLY IN ALPHANUMERIC MODE.", SymbolType.Normal, ErrorCorrection.M);
            code.Save("Test.png", 4);
        }
    }
}
