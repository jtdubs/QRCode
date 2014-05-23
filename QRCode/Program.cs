using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace QRCode
{
    class Program
    {
        public static void Main(string[] args)
        {
            var code = new QRCode("Hello, world!", SymbolType.Normal, ErrorCorrection.H);
            code.Show();

            Console.ReadLine();
        }
    }
}
