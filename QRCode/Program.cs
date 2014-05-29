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
            
            code = new QRCode("01234567");
            code.Save("Small Numeric.png", 4);

            code = new QRCode("SMALL TEXT");
            code.Save("Small Text.png", 4);

            code = new QRCode("12345678912345678912345679");
            code.Save("Longer Numeric.png", 4);

            code = new QRCode("MORE TEXT THAT IS LONGER AND NEEDS A BIGGER CODE");
            code.Save("Longer Text.png", 4);

            code = new QRCode("Bytes needed.");
            code.Save("Small Bytes.png", 4);

            code = new QRCode("This is a longer message that will take a bigger QR code to fit.  The small ones just won't be big enough.  Lets see what happens.");
            code.Save("Longer Bytes.png", 4);
        }
    }
}
