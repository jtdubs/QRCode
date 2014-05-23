using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace QRCode
{
    public enum ModuleType
    {
        Light, 
        Dark
    }
    
    public enum Mode
    {
        ECI,
        Numeric,
        AlphaNumeric, 
        Byte,
        Kanji,
        StructuredAppend,
        FNC1_FirstPosition,
        FNC1_SecondPosition,
        Terminator
    }
    
    public enum SymbolType 
    {
        Micro, 
        Normal 
    }

    public enum ErrorCorrection
    {
        [Description("L (7%)")]
        L,
        [Description("M (15%)")]
        M,
        [Description("Q (25%)")]
        Q,
        [Description("H (30%)")]
        H
    }

    public class QRCode
    {
        public QRCode(string data, SymbolType type, ErrorCorrection errorCorrection)
            : this(System.Text.Encoding.UTF8.GetBytes(data), type, errorCorrection)
        {
        }

        public QRCode(byte[] data, SymbolType type, ErrorCorrection errorCorrection)
        {
            Type = type;
            ErrorCorrection = errorCorrection;
            
            // encode the data into a bitstream
            List<BitArray> bitstream = Encode(data);
            int bitstreamLength = bitstream.Sum(b => b.Length);

            // find the best-fit QR version
            int capacity = ChooseVersion(bitstreamLength);

            // pad the bitstream to fill the data capacity of the chosen version
            Pad(bitstream, ref bitstreamLength, capacity);

            // draw the qr code
            Draw();
        }

        public SymbolType Type { get; private set; }
        public int Version { get; private set; }
        public ErrorCorrection ErrorCorrection { get; private set; }

        public string Description
        {
            get
            {
                switch (Type)
                {
                    case SymbolType.Micro:
                        return String.Format("QR M{0}", Version);
                    case SymbolType.Normal:
                        return String.Format("QR {0}", Version);
                }

                throw new InvalidOperationException();
            }
        }

        public void Show()
        {
            for (int y = 0; y < fullDim; y++)
            {
                for (int x = 0; x < fullDim; x++)
                {
                    if (modules[x, y] == ModuleType.Dark)
                        Console.Write("#");
                    else
                        Console.Write(".");
                }
                Console.WriteLine();
            }
        }

        #region Drawing
        private void Draw()
        {
            dim = GetSymbolDimension();
            qz = GetQuietZoneDimension();
            fullDim = dim + 2 * qz;

            // initialize to a full symbol of unaccessed, light modules
            accessCount = new int[fullDim, fullDim];
            modules = new ModuleType[fullDim, fullDim];
            for (int x = 0; x < fullDim; x++)
            {
                for (int y = 0; y < fullDim; y++)
                {
                    modules[x, y] = ModuleType.Light;
                    accessCount[x, y] = 0;
                }
            }

            // draw top-left finder pattern
            DrawFinderPattern(3, 3);

            switch (Type)
            {
                case SymbolType.Micro:
                    // draw timing lines
                    DrawTimingHLine(8, 0, dim - 8);
                    DrawTimingVLine(0, 8, dim - 8);
                    break;

                case SymbolType.Normal:
                    // draw top-right finder pattern
                    DrawFinderPattern(dim - 4, 3);

                    // draw bottom-left finder pattern
                    DrawFinderPattern(3, dim - 4);

                    // draw timing lines
                    DrawTimingHLine(8, 6, dim - 8);
                    DrawTimingVLine(6, 8, dim - 8 - 8);
                    break;
            }

            // draw alignment patterns
            foreach (var location in GetAlignmentPatternLocations())
            {
                // check for overlap with top-left finder pattern
                if (location.Item1 < 10 && location.Item2 < 10)
                    continue;

                // check for overlap with bottom-left finder pattern
                if (location.Item1 < 10 && location.Item2 > (dim - 10))
                    continue;

                // check for overlap with top-right finder pattern
                if (location.Item1 > (dim - 10) && location.Item2 < 10)
                    continue;

                DrawAlignmentPattern(location.Item1, location.Item2);
            }
        }

        private void DrawFinderPattern(int centerX, int centerY)
        {
            DrawRect(centerX-3, centerY-3, 7, 7, ModuleType.Dark);
            FillRect(centerX-1, centerY-1, 3, 3, ModuleType.Dark);
        }

        private void DrawAlignmentPattern(int centerX, int centerY)
        {
            DrawRect(centerX-2, centerY-2, 5, 5, ModuleType.Dark);
            Set(centerX, centerY, ModuleType.Dark);
        }

        private void FillRect(int left, int top, int width, int height, ModuleType type)
        {
            for (int dx = 0; dx < width; dx++)
                for (int dy = 0; dy < height; dy++)
                    Set(left + dx, top + dy, type);
        }

        private void DrawRect(int left, int top, int width, int height, ModuleType type)
        {
            DrawHLine(left, top, width, type);
            DrawHLine(left, top + height - 1, width, type);
            DrawVLine(left, top + 1, height - 2, type);
            DrawVLine(left + width - 1, top + 1, height - 2, type);
        }

        private void DrawHLine(int x, int y, int length, ModuleType type)
        {
            for (int dx = 0; dx < length; dx++)
                Set(x + dx, y, type);
        }

        private void DrawVLine(int x, int y, int length, ModuleType type)
        {
            for (int dy = 0; dy < length; dy++)
                Set(x, y + dy, type);
        }

        private void DrawTimingHLine(int x, int y, int length)
        {
            // advance to first dark module
            if (x % 2 == 1)
            {
                x++;
                length--;
            }

            for (int dx = 0; dx < length; dx+=2)
                Set(x + dx, y, ModuleType.Dark);
        }

        private void DrawTimingVLine(int x, int y, int length)
        {
            // advance to first dark module
            if (y % 2 == 1)
            {
                y++;
                length--;
            }

            for (int dy = 0; dy < length; dy+=2)
                Set(x, y + dy, ModuleType.Dark);
        }

        private void Set(int x, int y, ModuleType type)
        {
            modules[qz+x, qz+y] = type;
            accessCount[qz+x, qz+y]++;
        }
        #endregion

        #region Arithmetic
        private int GetSymbolDimension()
        {
            switch (Type)
            {
                case SymbolType.Micro:
                    return 9 + (2 * Version);
                case SymbolType.Normal:
                    return 17 + (4 * Version);
            }

            throw new InvalidOperationException();
        }

        private int GetQuietZoneDimension()
        {
            switch (Type)
            {
                case SymbolType.Micro:
                    return 2;
                case SymbolType.Normal:
                    return 4;
            }

            throw new InvalidOperationException();
        }

        private IEnumerable<Tuple<int, int>> GetAlignmentPatternLocations()
        {
            switch (Type)
            {
                case SymbolType.Micro:
                    break;

                case SymbolType.Normal:
                    var locations = AlignmentPatternLocations[Version-1];
                    for (int i=0; i<locations.Length; i++)
                    {
                        for (int j=i; j<locations.Length; j++)
                        {
                            yield return Tuple.Create(locations[i], locations[j]);
                            if (i != j)
                                yield return Tuple.Create(locations[j], locations[i]);
                        }
                    }
                    break;

                default:
                    throw new InvalidOperationException();
            }
        }
        #endregion

        #region Encoding
        private List<BitArray> Encode(byte[] data)
        {
            int idx = 0;

            List<BitArray> bits = new List<BitArray>();
            int maxRun = GetMaxCharacters(Mode.Byte);
            while ((data.Length - idx) > 0)
            {
                int thisRunLength = Math.Min(data.Length - idx, maxRun);
                byte[] thisRun = new byte[thisRunLength];
                Array.Copy(data, idx, thisRun, 0, thisRunLength);
                idx += thisRunLength;

                bits.Add(EncodeMode(Mode.Byte));
                bits.Add(EncodeCharacterCount(Mode.Byte, thisRunLength));
                bits.Add(new BitArray(thisRun));
            }

            bits.Add(EncodeMode(Mode.Terminator));

            return bits;
        }

        private int ChooseVersion(int totalBits)
        {
            int[] capacities = new int[0];

            switch (Type)
            {
                case SymbolType.Micro:
                    switch (ErrorCorrection)
                    {
                        case ErrorCorrection.L:
                            capacities = new int[]
                            {
                                0, 40, 84, 128
                            };
                            break;

                        case ErrorCorrection.M:
                            capacities = new int[]
                            {
                                0, 32, 68, 112
                            };
                            break;

                        case ErrorCorrection.Q:
                            capacities = new int[]
                            {
                                0, 0, 0, 80
                            };
                            break;
                    }
                    break;

                case SymbolType.Normal:
                    switch (ErrorCorrection)
                    {
                        case ErrorCorrection.L:
                            capacities = new int[]
                            {
                                  152,   272,   440,   640,   864,  1088,  1248,  1552,  1856,  2192,
                                 2592,  2960,  3424,  3688,  4184,  4712,  5176,  5768,  6360,  6888, 
                                 7456,  8048,  8752,  9392, 10208, 10960, 11744, 12248, 13048, 13880,
                                14744, 15640, 16568, 17528, 18448, 19472, 20528, 21616, 22496, 23648
                            };
                            break;

                        case ErrorCorrection.M:
                            capacities = new int[]
                            {
                                  128,   224,   352,   512,   688,   864,   992,  1232,  1456,  1728,
                                 2032,  2320,  2672,  2920,  3320,  3624,  4056,  4504,  5016,  5352, 
                                 5712,  6256,  6880,  7312,  8000,  8496,  9024,  9544, 10136, 10984,
                                11640, 12328, 13048, 13800, 14496, 15312, 15936, 16816, 17728, 18672
                            };
                            break;

                        case ErrorCorrection.Q:
                            capacities = new int[]
                            {
                                  104,  176,  272,  384,   496,   608,   704,   880,  1056,  1232,
                                 1440, 1648, 1952, 2088,  2360,  2600,  2936,  3176,  3560,  3880,
                                 4096, 4544, 4912, 5312,  5744,  6032,  6464,  6968,  7288,  7880,
                                 8264, 8920, 9368, 9848, 10288, 10832, 11408, 12016, 12656, 13328
                            };
                            break;

                        case ErrorCorrection.H:
                            capacities = new int[]
                            {
                                   72,  128,  208,  288,  368,  480,  528,  688,  800,   976,
                                 1120, 1264, 1440, 1576, 1784, 2024, 2264, 2504, 2728,  3080,
                                 3248, 3536, 3712, 4112, 4304, 4768, 5024, 5288, 5608,  5960,
                                 6344, 6760, 7208, 7688, 7888, 8432, 8768, 9136, 9776, 10208
                            };
                            break;
                    }
                    break;
            }

            try
            {
                Version = Enumerable.Range(0, capacities.Length).First(v => capacities[v] >= totalBits) + 1;
                return capacities[Version - 1];
            }
            catch
            {
                throw new ArgumentException("totalBits");
            }
        }

        private void Pad(List<BitArray> bitstream, ref int bitstreamLength, int capacity)
        {
            // pad the bitstream to the nearest octet boundary with zeroes
            if (bitstreamLength < capacity && bitstreamLength % 8 != 0)
            {
                int paddingLength = Math.Min(8 - (bitstreamLength % 8), capacity - bitstreamLength);
                bitstream.Add(new BitArray(paddingLength));
                bitstreamLength += paddingLength;
            }

            // fill the bitstream with pad codewords
            byte[] padCodewords = new byte[] { 0xEC, 0x11 };
            int i = 0;
            while (bitstreamLength < (capacity - 4))
            {
                bitstream.Add(new BitArray(padCodewords[i]));
                bitstreamLength += 8;
                i = (i + 1) % 2;
            }

            // fill the last nibble with zeroes (only necessary for M1 and M3)
            if (bitstreamLength < capacity)
            {
                bitstream.Add(new BitArray(4));
                bitstreamLength += 4;
            }
        }
        #endregion

        #region Helpers
        private IEnumerable<Mode> AvailableModes
        {
            get
            {
                switch (Type)
                {
                    case SymbolType.Normal:
                        yield return Mode.ECI;
                        yield return Mode.Numeric;
                        yield return Mode.AlphaNumeric;
                        yield return Mode.Byte;
                        yield return Mode.Kanji;
                        yield return Mode.StructuredAppend;
                        yield return Mode.FNC1_FirstPosition;
                        yield return Mode.FNC1_SecondPosition;
                        yield return Mode.Terminator;
                        break;

                    case SymbolType.Micro:
                        switch (Version)
                        {
                            case 1:
                                yield return Mode.Numeric;
                                yield return Mode.Terminator;
                                break;

                            case 2:
                                yield return Mode.Numeric;
                                yield return Mode.AlphaNumeric;
                                yield return Mode.Terminator;
                                break;

                            case 3:
                            case 4:
                                yield return Mode.Numeric;
                                yield return Mode.AlphaNumeric;
                                yield return Mode.Byte;
                                yield return Mode.Kanji;
                                yield return Mode.Terminator;
                                break;

                            default:
                                throw new InvalidOperationException();
                        }
                        break;

                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public BitArray EncodeMode(Mode mode)
        {
            if (!AvailableModes.Contains(mode))
                throw new ArgumentException("mode");

            switch (Type)
            {
                case SymbolType.Normal:
                    switch (mode)
                    {
                        case Mode.ECI:
                            return new BitArray(new bool[] { false, true, true, true });
                        case Mode.Numeric:
                            return new BitArray(new bool[] { false, false, false, true });
                        case Mode.AlphaNumeric:
                            return new BitArray(new bool[] { false, false, true, false });
                        case Mode.Byte:
                            return new BitArray(new bool[] { false, true, false, false });
                        case Mode.Kanji:
                            return new BitArray(new bool[] { true, false, false, false });
                        case Mode.FNC1_FirstPosition:
                            return new BitArray(new bool[] { false, true, false, true });
                        case Mode.FNC1_SecondPosition:
                            return new BitArray(new bool[] { true, false, false, true });
                        case Mode.Terminator:
                            return new BitArray(new bool[] { false, false, false, false });
                    }
                    break;

                case SymbolType.Micro:
                    switch (Version)
                    {
                        case 1:
                            switch (mode)
                            {
                                case Mode.Numeric:
                                    return new BitArray(0);
                                case Mode.Terminator:
                                    return new BitArray(new bool[] { false, false, false });
                            }
                            break;

                        case 2:
                            switch (mode)
                            {
                                case Mode.Numeric:
                                    return new BitArray(new bool[] { false });
                                case Mode.AlphaNumeric:
                                    return new BitArray(new bool[] { true });
                                case Mode.Terminator:
                                    return new BitArray(new bool[] { false, false, false, false, false });
                            }
                            break;

                        case 3:
                            switch (mode)
                            {
                                case Mode.Numeric:
                                    return new BitArray(new bool[] { false, false });
                                case Mode.AlphaNumeric:
                                    return new BitArray(new bool[] { false, true });
                                case Mode.Byte:
                                    return new BitArray(new bool[] { true, false });
                                case Mode.Kanji:
                                    return new BitArray(new bool[] { true, true });
                                case Mode.Terminator:
                                    return new BitArray(new bool[] { false, false, false, false, false, false, false });
                            }
                            break;

                        case 4:
                            switch (mode)
                            {
                                case Mode.Numeric:
                                    return new BitArray(new bool[] { false, false, false });
                                case Mode.AlphaNumeric:
                                    return new BitArray(new bool[] { false, false, true });
                                case Mode.Byte:
                                    return new BitArray(new bool[] { false, true, false });
                                case Mode.Kanji:
                                    return new BitArray(new bool[] { false, true, true });
                                case Mode.Terminator:
                                    return new BitArray(new bool[] { false, false, false, false, false, false, false, false, false });
                            }
                            break;
                    }
                    break;
                
            }

            throw new InvalidOperationException();
        }

        private BitArray EncodeCharacterCount(Mode mode, int count)
        {
            int bits = GetCharacterCountBits(mode);

            int min = 1;
            int max = GetMaxCharacters(mode);

            if (count < min || count > max)
                throw new ArgumentOutOfRangeException("count", count, String.Format("QR {0} character counts must be in the range {1} <= n <= {2}", Description, min, max));

            var result = new BitArray(bits);
            for (int i = 0; i < bits; i++)
                result.Set(i, (count & (1 << i)) != 0);
            return result;
        }

        private int GetCharacterCountBits(Mode mode)
        {
            switch (Type)
            {
                case SymbolType.Micro:
                    switch (Version)
                    {
                        case 1:
                            switch (mode)
                            {
                                case Mode.Numeric:
                                    return 3;
                            }
                            break;

                        case 2:
                            switch (mode)
                            {
                                case Mode.Numeric:
                                    return 4;
                                case Mode.AlphaNumeric:
                                    return 3;
                            }
                            break;

                        case 3:
                            switch (mode)
                            {
                                case Mode.Numeric:
                                    return 5;
                                case Mode.AlphaNumeric:
                                    return 4;
                                case Mode.Byte:
                                    return 4;
                                case Mode.Kanji:
                                    return 3;
                            }
                            break;

                        case 4:
                            switch (mode)
                            {
                                case Mode.Numeric:
                                    return 6;
                                case Mode.AlphaNumeric:
                                    return 5;
                                case Mode.Byte:
                                    return 5;
                                case Mode.Kanji:
                                    return 4;
                            }
                            break;
                    }
                    break;

                case SymbolType.Normal:
                    if (Version <= 9)
                    {
                        switch (mode)
                        {
                            case Mode.Numeric:
                                return 10;
                            case Mode.AlphaNumeric:
                                return 9;
                            case Mode.Byte:
                                return 8;
                            case Mode.Kanji:
                                return 8;
                        }
                    }
                    else if (Version <= 26)
                    {
                        switch (mode)
                        {
                            case Mode.Numeric:
                                return 12;
                            case Mode.AlphaNumeric:
                                return 11;
                            case Mode.Byte:
                                return 16;
                            case Mode.Kanji:
                                return 10;
                        }
                    }
                    else if (Version <= 40)
                    {
                        switch (mode)
                        {
                            case Mode.Numeric:
                                return 14;
                            case Mode.AlphaNumeric:
                                return 13;
                            case Mode.Byte:
                                return 16;
                            case Mode.Kanji:
                                return 12;
                        }
                    }
                    break;
            }

            throw new InvalidOperationException();
        }

        private int GetMaxCharacters(Mode mode)
        {
            return (1 << GetCharacterCountBits(mode)) - 1;
        }
        #endregion

        #region Data
        /*
        private static IEnumerable<char> GetValidCharacters(Mode mode)
        {
            switch (mode) 
            {
                case Mode.Numeric:
                    return new char[] 
                    {
                        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' 
                    };
                case Mode.AlphaNumeric:
                    return new char[] 
                    {
                        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
                        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
                        'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
                        ' ', '$', '%', '*', '+', '-', '.', '/', ':'
                    };
                default:
                    throw new ArgumentException("charSet");
            }
        }
        */

        private static int[][] AlignmentPatternLocations = new int[][]
        {
            new int[] { },
            new int[] { 6, 18 },
            new int[] { 6, 22 },
            new int[] { 6, 26 },
            new int[] { 6, 30 },
            new int[] { 6, 34 },
            new int[] { 6, 22, 38 },
            new int[] { 6, 24, 42 },
            new int[] { 6, 26, 46 },
            new int[] { 6, 28, 50 },
            new int[] { 6, 30, 54 },
            new int[] { 6, 32, 58 },
            new int[] { 6, 34, 62 },
            new int[] { 6, 26, 46, 66 },
            new int[] { 6, 26, 48, 70 },
            new int[] { 6, 26, 50, 74 },
            new int[] { 6, 30, 54, 78 },
            new int[] { 6, 30, 56, 82 },
            new int[] { 6, 30, 58, 86 },
            new int[] { 6, 34, 62, 90 },
            new int[] { 6, 28, 50, 72, 94 },
            new int[] { 6, 26, 50, 74, 98 },
            new int[] { 6, 30, 54, 78, 102 },
            new int[] { 6, 28, 54, 80, 106 },
            new int[] { 6, 32, 58, 84, 110 },
            new int[] { 6, 30, 58, 86, 114 },
            new int[] { 6, 34, 62, 90, 118 },
            new int[] { 6, 26, 50, 74, 98, 122 },
            new int[] { 6, 30, 54, 78, 102, 126 },
            new int[] { 6, 26, 52, 78, 104, 130 },
            new int[] { 6, 30, 56, 82, 108, 134 },
            new int[] { 6, 34, 60, 86, 112, 138 },
            new int[] { 6, 30, 58, 86, 114, 142 },
            new int[] { 6, 34, 62, 90, 118, 146 },
            new int[] { 6, 30, 54, 78, 102, 126, 150 },
            new int[] { 6, 24, 50, 76, 102, 128, 154 },
            new int[] { 6, 28, 54, 80, 106, 132, 158 },
            new int[] { 6, 32, 58, 84, 110, 136, 162 },
            new int[] { 6, 26, 54, 82, 110, 138, 166 },
            new int[] { 6, 30, 58, 86, 114, 142, 170 },
        };
        #endregion

        // (0, 0) - top, left
        // (w, h) - bottom, right 
        private ModuleType[,] modules;
        private int[,] accessCount;
        private int dim;
        private int fullDim;
        private int qz;
    }
}
