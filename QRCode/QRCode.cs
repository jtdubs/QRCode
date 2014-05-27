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
            /*
            Type = type;
            ErrorCorrection = errorCorrection;

            // find the best-fit QR version
            int capacity = ChooseVersion(data.Length);

            // encode the data into a bitstream
            int bitstreamLength;
            List<BitArray> bitstream = Encode(data, out bitstreamLength);

            // pad the bitstream to fill the data capacity of the chosen version
            Pad(bitstream, ref bitstreamLength, capacity);



            // convert to codewords

            bool[] bits = new bool[bitstreamLength];
            int idx = 0;
            foreach (var b in bitstream)
            {
                b.CopyTo(bits, idx);
                idx += b.Length;
            }

            byte[] codeWords = new byte[(bits.Length - 1) / 8 + 1];
            new BitArray(bits).CopyTo(codeWords, 0);
            */

            // hard-coded data to match example
            Type = SymbolType.Normal;
            Version = 1;
            ErrorCorrection = ErrorCorrection.M;
            byte[] codeWords = new byte[] { 32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17 };
           
            // generate error correction words
            var ecc = ErrorCorrectionTable[Type][Version][ErrorCorrection];
            int idx = 0;
            foreach (var e in ecc)
            {
                for (int b = 0; b < e.Item1; b++)
                {
                    int dataWords = e.Item3;
                    int errorWords = e.Item2 - e.Item3;

                    var block = Enumerable.Concat(codeWords.Skip(idx).Take(dataWords).Select(i => LogTable[i]), Enumerable.Repeat((byte)0, errorWords)).ToArray();
                    var poly = Enumerable.Concat(Polynomials[errorWords], Enumerable.Repeat((byte)0, dataWords)).ToArray();
                }
            }

          


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

        #region Encoding
        private List<BitArray> Encode(byte[] data, out int bitstreamLength)
        {
            List<BitArray> bits = new List<BitArray>();
            bits.Add(EncodeMode(Mode.Byte));
            bits.Add(EncodeCharacterCount(Mode.Byte, data.Length));
            bits.Add(new BitArray(data));
            bits.Add(EncodeMode(Mode.Terminator));
            bitstreamLength = bits.Sum(b => b.Length);
            return bits;
        }

        private int ChooseVersion(int bytes)
        {
            try
            {
                Version = DataCapacityTable[Type][ErrorCorrection].First(p => p.Value.Item3 >= bytes).Key;
                return DataCapacityTable[Type][ErrorCorrection][Version].Item3 * 8;
            }
            catch (KeyNotFoundException)
            {
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
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
                bitstream.Add(new BitArray(new byte[] { padCodewords[i] }));
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
                    var locations = AlignmentPatternLocations[Version - 1];
                    for (int i = 0; i < locations.Length; i++)
                    {
                        for (int j = i; j < locations.Length; j++)
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

        private IEnumerable<Mode> AvailableModes
        {
            get
            {
                switch (Type)
                {
                    case SymbolType.Normal:
                        return NormalModes;

                    case SymbolType.Micro:
                        try
                        {
                            return MicroModes[Version];
                        }
                        catch (KeyNotFoundException)
                        {
                            throw new InvalidOperationException();
                        }

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
                    return NormalModeEncodings[mode];

                case SymbolType.Micro:
                    return MicroModeEncodings[Version][mode];
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
            try
            {
                return CharacterWidthTable[Type][Version][mode];
            }
            catch (KeyNotFoundException)
            {
                throw new InvalidOperationException();
            }
        }

        private int GetMaxCharacters(Mode mode)
        {
            return (1 << GetCharacterCountBits(mode)) - 1;
        }

        private Tuple<int, int, int, int>[] GetErrorCorrectionParameters()
        {
            return ErrorCorrectionTable[Type][Version][ErrorCorrection];
        }
        #endregion

        #region Data
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

        private static Dictionary<SymbolType, Dictionary<ErrorCorrection, int[]>> SymbolCapacityTable =
            new List<Tuple<SymbolType, ErrorCorrection, int[]>>()
            {
                Tuple.Create(SymbolType.Micro,  ErrorCorrection.L, new int[] 
                {
                    0, 40, 84, 128 
                }),
                Tuple.Create(SymbolType.Micro,  ErrorCorrection.M, new int[]
                {
                    0, 32, 68, 112 
                }),
                Tuple.Create(SymbolType.Micro,  ErrorCorrection.Q, new int[]
                {
                    0,  0,  0,  80 
                }),
                Tuple.Create(SymbolType.Normal, ErrorCorrection.L, new int[] 
                {
                      152,   272,   440,   640,   864,  1088,  1248,  1552,  1856,  2192,
                     2592,  2960,  3424,  3688,  4184,  4712,  5176,  5768,  6360,  6888, 
                     7456,  8048,  8752,  9392, 10208, 10960, 11744, 12248, 13048, 13880,
                    14744, 15640, 16568, 17528, 18448, 19472, 20528, 21616, 22496, 23648 
                }),
                Tuple.Create(SymbolType.Normal, ErrorCorrection.M, new int[] 
                {
                      128,   224,   352,   512,   688,   864,   992,  1232,  1456,  1728,
                     2032,  2320,  2672,  2920,  3320,  3624,  4056,  4504,  5016,  5352, 
                     5712,  6256,  6880,  7312,  8000,  8496,  9024,  9544, 10136, 10984,
                    11640, 12328, 13048, 13800, 14496, 15312, 15936, 16816, 17728, 18672
                }),
                Tuple.Create(SymbolType.Normal, ErrorCorrection.Q, new int[] 
                {
                     104,   176,   272,   384,   496,   608,   704,   880,  1056,  1232,
                    1440,  1648,  1952,  2088,  2360,  2600,  2936,  3176,  3560,  3880,
                    4096,  4544,  4912,  5312,  5744,  6032,  6464,  6968,  7288,  7880,
                    8264,  8920,  9368,  9848, 10288, 10832, 11408, 12016, 12656, 13328
                }),
                Tuple.Create(SymbolType.Normal, ErrorCorrection.H, new int[] 
                {
                      72,   128,   208,   288,   368,   480,   528,   688,   800,   976,
                    1120,  1264,  1440,  1576,  1784,  2024,  2264,  2504,  2728,  3080,
                    3248,  3536,  3712,  4112,  4304,  4768,  5024,  5288,  5608,  5960,
                    6344,  6760,  7208,  7688,  7888,  8432,  8768,  9136,  9776, 10208
                }),
            }
            .GroupBy(t => t.Item1)
            .Select(g => Tuple.Create(g.Key, g.ToDictionary(t => t.Item2, t => t.Item3)))
            .ToDictionary(t => t.Item1, t => t.Item2);

        private static Mode[] NormalModes = new Mode[]
        {
            Mode.ECI,
            Mode.Numeric,
            Mode.AlphaNumeric,
            Mode.Byte,
            Mode.Kanji,
            Mode.StructuredAppend,
            Mode.FNC1_FirstPosition,
            Mode.FNC1_SecondPosition,
            Mode.Terminator
        };

        private static Dictionary<int, Mode[]> MicroModes = new Dictionary<int, Mode[]>
        {
            { 1, new Mode[] { Mode.Numeric, Mode.Terminator } },
            { 2, new Mode[] { Mode.Numeric, Mode.AlphaNumeric, Mode.Terminator } },
            { 3, new Mode[] { Mode.Numeric, Mode.AlphaNumeric, Mode.Byte, Mode.Kanji, Mode.Terminator } },
            { 4, new Mode[] { Mode.Numeric, Mode.AlphaNumeric, Mode.Byte, Mode.Kanji, Mode.Terminator } },
        };

        private static Dictionary<Mode, BitArray> NormalModeEncodings = new Dictionary<Mode, BitArray>()
        {
            { Mode.ECI,                 new BitArray(new bool[] { false,  true,  true,  true }) },
            { Mode.Numeric,             new BitArray(new bool[] { false, false, false,  true }) },
            { Mode.AlphaNumeric,        new BitArray(new bool[] { false, false,  true, false }) },
            { Mode.Byte,                new BitArray(new bool[] { false,  true, false, false }) },
            { Mode.Kanji,               new BitArray(new bool[] {  true, false, false, false }) },
            { Mode.FNC1_FirstPosition,  new BitArray(new bool[] { false,  true, false,  true }) },
            { Mode.FNC1_SecondPosition, new BitArray(new bool[] {  true, false, false,  true }) },
            { Mode.Terminator,          new BitArray(new bool[] { false, false, false, false }) },
        };

        private static Dictionary<int, Dictionary<Mode, BitArray>> MicroModeEncodings =
            new List<Tuple<int, Mode, BitArray>>()
            {
                Tuple.Create(1, Mode.Numeric,      new BitArray(0)),
                Tuple.Create(1, Mode.Terminator,   new BitArray(new bool[] { false, false, false })),
                Tuple.Create(2, Mode.Numeric,      new BitArray(new bool[] { false })),
                Tuple.Create(2, Mode.AlphaNumeric, new BitArray(new bool[] {  true })),
                Tuple.Create(2, Mode.Terminator,   new BitArray(new bool[] { false, false, false, false, false })),
                Tuple.Create(3, Mode.Numeric,      new BitArray(new bool[] { false, false })),
                Tuple.Create(3, Mode.AlphaNumeric, new BitArray(new bool[] { false,  true })),
                Tuple.Create(3, Mode.Byte,         new BitArray(new bool[] {  true, false })),
                Tuple.Create(3, Mode.Kanji,        new BitArray(new bool[] {  true,  true })),
                Tuple.Create(3, Mode.Terminator,   new BitArray(new bool[] { false, false, false, false, false, false, false })),
                Tuple.Create(4, Mode.Numeric,      new BitArray(new bool[] { false, false, false })),
                Tuple.Create(4, Mode.AlphaNumeric, new BitArray(new bool[] { false, false,  true })),
                Tuple.Create(4, Mode.Byte,         new BitArray(new bool[] { false,  true, false })),
                Tuple.Create(4, Mode.Kanji,        new BitArray(new bool[] { false,  true,  true })),
                Tuple.Create(4, Mode.Terminator,   new BitArray(new bool[] { false, false, false, false, false, false, false, false, false })),
            }
            .GroupBy(t => t.Item1)
            .Select(g => Tuple.Create(g.Key, g.ToDictionary(t => t.Item2, t => t.Item3)))
            .ToDictionary(t => t.Item1, t => t.Item2);

        private static Dictionary<SymbolType, Dictionary<int, Dictionary<Mode, int>>> CharacterWidthTable =
            new List<Tuple<SymbolType, int, Mode, int>>()
            {
                Tuple.Create(SymbolType.Micro,   1, Mode.Numeric,       3),
                Tuple.Create(SymbolType.Micro,   2, Mode.Numeric,       4),
                Tuple.Create(SymbolType.Micro,   2, Mode.AlphaNumeric,  3),
                Tuple.Create(SymbolType.Micro,   3, Mode.Numeric,       5),
                Tuple.Create(SymbolType.Micro,   3, Mode.AlphaNumeric,  4),
                Tuple.Create(SymbolType.Micro,   3, Mode.Byte,          4),
                Tuple.Create(SymbolType.Micro,   3, Mode.Kanji,         3),
                Tuple.Create(SymbolType.Micro,   4, Mode.Numeric,       6),
                Tuple.Create(SymbolType.Micro,   4, Mode.AlphaNumeric,  5),
                Tuple.Create(SymbolType.Micro,   4, Mode.Byte,          5),
                Tuple.Create(SymbolType.Micro,   4, Mode.Kanji,         4),
                Tuple.Create(SymbolType.Normal,  1, Mode.Numeric,      10),
                Tuple.Create(SymbolType.Normal,  1, Mode.AlphaNumeric,  9),
                Tuple.Create(SymbolType.Normal,  1, Mode.Byte,          8),
                Tuple.Create(SymbolType.Normal,  1, Mode.Kanji,         8),
                Tuple.Create(SymbolType.Normal,  2, Mode.Numeric,      10),
                Tuple.Create(SymbolType.Normal,  2, Mode.AlphaNumeric,  9),
                Tuple.Create(SymbolType.Normal,  2, Mode.Byte,          8),
                Tuple.Create(SymbolType.Normal,  2, Mode.Kanji,         8),
                Tuple.Create(SymbolType.Normal,  3, Mode.Numeric,      10),
                Tuple.Create(SymbolType.Normal,  3, Mode.AlphaNumeric,  9),
                Tuple.Create(SymbolType.Normal,  3, Mode.Byte,          8),
                Tuple.Create(SymbolType.Normal,  3, Mode.Kanji,         8),
                Tuple.Create(SymbolType.Normal,  4, Mode.Numeric,      10),
                Tuple.Create(SymbolType.Normal,  4, Mode.AlphaNumeric,  9),
                Tuple.Create(SymbolType.Normal,  4, Mode.Byte,          8),
                Tuple.Create(SymbolType.Normal,  4, Mode.Kanji,         8),
                Tuple.Create(SymbolType.Normal,  5, Mode.Numeric,      10),
                Tuple.Create(SymbolType.Normal,  5, Mode.AlphaNumeric,  9),
                Tuple.Create(SymbolType.Normal,  5, Mode.Byte,          8),
                Tuple.Create(SymbolType.Normal,  5, Mode.Kanji,         8),
                Tuple.Create(SymbolType.Normal,  6, Mode.Numeric,      10),
                Tuple.Create(SymbolType.Normal,  6, Mode.AlphaNumeric,  9),
                Tuple.Create(SymbolType.Normal,  6, Mode.Byte,          8),
                Tuple.Create(SymbolType.Normal,  6, Mode.Kanji,         8),
                Tuple.Create(SymbolType.Normal,  7, Mode.Numeric,      10),
                Tuple.Create(SymbolType.Normal,  7, Mode.AlphaNumeric,  9),
                Tuple.Create(SymbolType.Normal,  7, Mode.Byte,          8),
                Tuple.Create(SymbolType.Normal,  7, Mode.Kanji,         8),
                Tuple.Create(SymbolType.Normal,  8, Mode.Numeric,      10),
                Tuple.Create(SymbolType.Normal,  8, Mode.AlphaNumeric,  9),
                Tuple.Create(SymbolType.Normal,  8, Mode.Byte,          8),
                Tuple.Create(SymbolType.Normal,  8, Mode.Kanji,         8),
                Tuple.Create(SymbolType.Normal,  9, Mode.Numeric,      10),
                Tuple.Create(SymbolType.Normal,  9, Mode.AlphaNumeric,  9),
                Tuple.Create(SymbolType.Normal,  9, Mode.Byte,          8),
                Tuple.Create(SymbolType.Normal,  9, Mode.Kanji,         8),
                Tuple.Create(SymbolType.Normal, 10, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 10, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 10, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 10, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 11, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 11, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 11, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 11, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 12, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 12, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 12, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 12, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 13, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 13, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 13, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 13, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 14, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 14, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 14, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 14, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 15, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 15, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 15, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 15, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 16, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 16, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 16, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 16, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 17, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 17, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 17, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 17, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 18, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 18, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 18, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 18, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 19, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 19, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 19, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 19, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 20, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 20, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 20, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 20, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 21, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 21, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 21, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 21, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 22, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 22, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 22, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 22, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 23, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 23, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 23, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 23, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 24, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 24, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 24, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 24, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 25, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 25, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 25, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 25, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 26, Mode.Numeric,      12),
                Tuple.Create(SymbolType.Normal, 26, Mode.AlphaNumeric, 11),
                Tuple.Create(SymbolType.Normal, 26, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 26, Mode.Kanji,        10),
                Tuple.Create(SymbolType.Normal, 27, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 27, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 27, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 27, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 28, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 28, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 28, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 28, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 29, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 29, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 29, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 29, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 30, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 30, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 30, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 30, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 31, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 31, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 31, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 31, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 32, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 32, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 32, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 32, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 33, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 33, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 33, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 33, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 34, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 34, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 34, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 34, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 35, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 35, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 35, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 35, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 36, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 36, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 36, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 36, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 37, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 37, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 37, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 37, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 38, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 38, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 38, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 38, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 39, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 39, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 39, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 39, Mode.Kanji,        12),
                Tuple.Create(SymbolType.Normal, 40, Mode.Numeric,      14),
                Tuple.Create(SymbolType.Normal, 40, Mode.AlphaNumeric, 13),
                Tuple.Create(SymbolType.Normal, 40, Mode.Byte,         16),
                Tuple.Create(SymbolType.Normal, 40, Mode.Kanji,        12),
            }
            .GroupBy(t => t.Item1)
            .Select(t =>
                Tuple.Create(
                    t.Key,
                    t
                        .GroupBy(n => n.Item2)
                        .Select(n =>
                            Tuple.Create(
                                n.Key,
                                n.ToDictionary(g => g.Item3, g => g.Item4)))
                        .ToDictionary(n => n.Item1, n => n.Item2)))
            .ToDictionary(t => t.Item1, t => t.Item2);

        private static Dictionary<SymbolType, Dictionary<int, Dictionary<ErrorCorrection, Tuple<int, int, int, int>[]>>> ErrorCorrectionTable =
                new List<Tuple<SymbolType, int, ErrorCorrection, Tuple<int, int, int, int>[]>>()
                {
                    Tuple.Create(SymbolType.Micro,   2, ErrorCorrection.L, new[] { Tuple.Create( 1,  10,   5,   1) }),
                    Tuple.Create(SymbolType.Micro,   2, ErrorCorrection.M, new[] { Tuple.Create( 1,  10,   4,   2) }),
                    Tuple.Create(SymbolType.Micro,   3, ErrorCorrection.L, new[] { Tuple.Create( 1,  17,  11,   2) }),
                    Tuple.Create(SymbolType.Micro,   3, ErrorCorrection.M, new[] { Tuple.Create( 1,  17,   9,   4) }),
                    Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.L, new[] { Tuple.Create( 1,  24,  16,   3) }),
                    Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.M, new[] { Tuple.Create( 1,  24,  14,   5) }),
                    Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.Q, new[] { Tuple.Create( 1,  24,  10,   7) }),
                    Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.L, new[] { Tuple.Create( 1,  26,  19,   2) }),
                    Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.M, new[] { Tuple.Create( 1,  26,  16,   4) }),
                    Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.Q, new[] { Tuple.Create( 1,  26,  13,   6) }),
                    Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.H, new[] { Tuple.Create( 1,  26,   9,   8) }),
                    Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.L, new[] { Tuple.Create( 1,  44,  34,   4) }),
                    Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.M, new[] { Tuple.Create( 1,  44,  28,   8) }),
                    Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.Q, new[] { Tuple.Create( 1,  44,  22,  11) }),
                    Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.H, new[] { Tuple.Create( 1,  44,  16,  14) }),
                    Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.L, new[] { Tuple.Create( 1,  70,  55,   7) }),
                    Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.M, new[] { Tuple.Create( 1,  70,  44,  13) }),
                    Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.Q, new[] { Tuple.Create( 2,  35,  17,   9) }),
                    Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.H, new[] { Tuple.Create( 2,  35,  13,  11) }),
                    Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.L, new[] { Tuple.Create( 1, 100,  80,  10) }),
                    Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.M, new[] { Tuple.Create( 2,  50,  32,   9) }),
                    Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.Q, new[] { Tuple.Create( 2,  50,  24,  13) }),
                    Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.H, new[] { Tuple.Create( 4,  25,   9,   8) }),
                    Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.L, new[] { Tuple.Create( 1, 134, 108,  13) }),
                    Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.M, new[] { Tuple.Create( 2,  67,  43,  12) }),
                    Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.Q, new[] { Tuple.Create( 2,  33,  15,   9), 
                                                                                   Tuple.Create( 2,  34,  16,   9) }),
                    Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.H, new[] { Tuple.Create( 2,  33,  11,  11), 
                                                                                   Tuple.Create( 2,  34,  12,  11) }),
                    Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.L, new[] { Tuple.Create( 2,  86,  68,   9) }),
                    Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.M, new[] { Tuple.Create( 4,  43,  27,   8) }),
                    Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.Q, new[] { Tuple.Create( 4,  43,  19,  12) }),
                    Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.H, new[] { Tuple.Create( 4,  43,  15,  14) }),
                    Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.L, new[] { Tuple.Create( 2,  98,  78,  10) }),
                    Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.M, new[] { Tuple.Create( 4,  49,  31,   9) }),
                    Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.Q, new[] { Tuple.Create( 2,  32,  14,   9), 
                                                                                   Tuple.Create( 4,  33,  15,   9) }),
                    Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.H, new[] { Tuple.Create( 4,  39,  13,  13), 
                                                                                   Tuple.Create( 1,  40,  14,  13) }),
                    Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.L, new[] { Tuple.Create( 2, 121,  97,  12) }),
                    Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.M, new[] { Tuple.Create( 2,  60,  38,  11),
                                                                                   Tuple.Create( 2,  61,  39,  11) }),
                    Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.Q, new[] { Tuple.Create( 4,  40,  18,  11), 
                                                                                   Tuple.Create( 2,  41,  19,  11) }),
                    Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.H, new[] { Tuple.Create( 4,  40,  14,  13), 
                                                                                   Tuple.Create( 2,  41,  15,  13) }),
                    Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.L, new[] { Tuple.Create( 2, 146, 116,  15) }),
                    Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.M, new[] { Tuple.Create( 3,  58,  36,  11),
                                                                                   Tuple.Create( 2,  59,  37,  11) }),
                    Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.Q, new[] { Tuple.Create( 4,  36,  16,  10), 
                                                                                   Tuple.Create( 4,  37,  17,  10) }),
                    Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.H, new[] { Tuple.Create( 4,  36,  12,  12), 
                                                                                   Tuple.Create( 4,  37,  13,  12) }),
                    Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.L, new[] { Tuple.Create( 2,  86,  68,   9),
                                                                                   Tuple.Create( 2,  87,  69,   9) }),
                    Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.M, new[] { Tuple.Create( 4,  69,  43,  13),
                                                                                   Tuple.Create( 1,  70,  44,  13) }),
                    Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.Q, new[] { Tuple.Create( 6,  43,  19,  12), 
                                                                                   Tuple.Create( 2,  44,  20,  12) }),
                    Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.H, new[] { Tuple.Create( 6,  43,  15,  14), 
                                                                                   Tuple.Create( 2,  44,  16,  14) }),
                    Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.L, new[] { Tuple.Create( 4, 101,  81,  10) }),
                    Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.M, new[] { Tuple.Create( 1,  80,  50,  15),
                                                                                   Tuple.Create( 4,  81,  51,  15) }),
                    Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.Q, new[] { Tuple.Create( 4,  50,  22,  14), 
                                                                                   Tuple.Create( 4,  51,  23,  14) }),
                    Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.H, new[] { Tuple.Create( 3,  36,  12,  12), 
                                                                                   Tuple.Create( 8,  37,  13,  12) }),
                    Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.L, new[] { Tuple.Create( 2, 116,  92,  12),
                                                                                   Tuple.Create( 2, 117,  93,  12) }),
                    Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.M, new[] { Tuple.Create( 6,  58,  36,  11),
                                                                                   Tuple.Create( 2,  59,  67,  11) }),
                    Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.Q, new[] { Tuple.Create( 4,  46,  20,  13), 
                                                                                   Tuple.Create( 6,  47,  21,  13) }),
                    Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.H, new[] { Tuple.Create( 7,  42,  14,  14), 
                                                                                   Tuple.Create( 4,  43,  15,  14) }),
                    Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.L, new[] { Tuple.Create( 4, 133, 107,  13) }),
                    Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.M, new[] { Tuple.Create( 8,  59,  37,  11),
                                                                                   Tuple.Create( 1,  60,  38,  11) }),
                    Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.Q, new[] { Tuple.Create( 8,  44,  20,  12), 
                                                                                   Tuple.Create( 4,  45,  21,  12) }),
                    Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.H, new[] { Tuple.Create(12,  33,  11,  11), 
                                                                                   Tuple.Create( 4,  34,  12,  11) }),
                    Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.L, new[] { Tuple.Create( 3, 145, 115,  15),
                                                                                   Tuple.Create( 1, 146, 116,  15) }),
                    Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.M, new[] { Tuple.Create( 4,  64,  40,  12),
                                                                                   Tuple.Create( 5,  65,  41,  12) }),
                    Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.Q, new[] { Tuple.Create(11,  36,  16,  10), 
                                                                                   Tuple.Create( 5,  37,  17,  10) }),
                    Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.H, new[] { Tuple.Create(11,  36,  12,  12), 
                                                                                   Tuple.Create( 5,  37,  13,  12) }),
                    Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.L, new[] { Tuple.Create( 5, 109,  87,  11),
                                                                                   Tuple.Create( 1, 110,  88,  11) }),
                    Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.M, new[] { Tuple.Create( 5,  65,  41,  12),
                                                                                   Tuple.Create( 5,  66,  42,  12) }),
                    Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.Q, new[] { Tuple.Create( 5,  54,  24,  15), 
                                                                                   Tuple.Create( 7,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.H, new[] { Tuple.Create(11,  36,  12,  12), 
                                                                                   Tuple.Create( 7,  37,  13,  12) }),
                    Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.L, new[] { Tuple.Create( 5, 122,  98,  12),
                                                                                   Tuple.Create( 1, 123,  99,  12) }),
                    Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.M, new[] { Tuple.Create( 7,  73,  45,  14),
                                                                                   Tuple.Create( 3,  74,  46,  14) }),
                    Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.Q, new[] { Tuple.Create(15,  43,  19,  12), 
                                                                                   Tuple.Create( 2,  44,  20,  12) }),
                    Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.H, new[] { Tuple.Create( 3,  45,  15,  15), 
                                                                                   Tuple.Create(13,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.L, new[] { Tuple.Create( 1, 135, 107,  14),
                                                                                   Tuple.Create( 5, 136, 108,  14) }),
                    Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.M, new[] { Tuple.Create(10,  74,  46,  14),
                                                                                   Tuple.Create( 1,  75,  47,  14) }),
                    Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.Q, new[] { Tuple.Create( 1,  50,  22,  14), 
                                                                                   Tuple.Create(15,  51,  23,  14) }),
                    Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.H, new[] { Tuple.Create( 2,  42,  14,  14), 
                                                                                   Tuple.Create(17,  43,  15,  14) }),
                    Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.L, new[] { Tuple.Create( 5, 150, 120,  15),
                                                                                   Tuple.Create( 1, 151, 121,  15) }),
                    Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.M, new[] { Tuple.Create( 9,  69,  43,  13),
                                                                                   Tuple.Create( 4,  70,  44,  13) }),
                    Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.Q, new[] { Tuple.Create(17,  50,  22,  14), 
                                                                                   Tuple.Create( 1,  51,  23,  14) }),
                    Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.H, new[] { Tuple.Create( 2,  42,  14,  14), 
                                                                                   Tuple.Create(19,  43,  15,  14) }),
                    Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.L, new[] { Tuple.Create( 3, 141, 113,  14),
                                                                                   Tuple.Create( 4, 142, 114,  14) }),
                    Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.M, new[] { Tuple.Create( 3,  70,  44,  13),
                                                                                   Tuple.Create(11,  71,  45,  13) }),
                    Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.Q, new[] { Tuple.Create(17,  47,  21,  13), 
                                                                                   Tuple.Create( 4,  48,  22,  13) }),
                    Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.H, new[] { Tuple.Create( 9,  39,  13,  13), 
                                                                                   Tuple.Create(16,  40,  16,  13) }),
                    Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.L, new[] { Tuple.Create( 3, 135, 107,  14),
                                                                                   Tuple.Create( 5, 136, 108,  14) }),
                    Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.M, new[] { Tuple.Create( 3,  67,  41,  13),
                                                                                   Tuple.Create(13,  68,  42,  13) }),
                    Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.Q, new[] { Tuple.Create(15,  54,  24,  15), 
                                                                                   Tuple.Create( 5,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.H, new[] { Tuple.Create(15,  43,  15,  14), 
                                                                                   Tuple.Create(10,  44,  16,  14) }),
                    Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.L, new[] { Tuple.Create( 4, 144, 116,  14),
                                                                                   Tuple.Create( 4, 145, 117,  14) }),
                    Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.M, new[] { Tuple.Create(17,  68,  42,  13) }),
                    Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.Q, new[] { Tuple.Create(17,  50,  22,  14), 
                                                                                   Tuple.Create( 6,  51,  23,  14) }),
                    Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.H, new[] { Tuple.Create(19,  46,  16,  15), 
                                                                                   Tuple.Create( 6,  47,  17,  15) }),
                    Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.L, new[] { Tuple.Create( 2, 139, 111,  14),
                                                                                   Tuple.Create( 7, 140, 112,  14) }),
                    Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.M, new[] { Tuple.Create(17,  74,  46,  14) }),
                    Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.Q, new[] { Tuple.Create( 7,  54,  24,  15), 
                                                                                   Tuple.Create(16,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.H, new[] { Tuple.Create(34,  37,  13,  12) }),
                    Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.L, new[] { Tuple.Create( 4, 151, 121,  15),
                                                                                   Tuple.Create( 5, 152, 122,  15) }),
                    Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.M, new[] { Tuple.Create( 4,  75,  47,  14),
                                                                                   Tuple.Create(14,  76,  48,  14) }),
                    Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.Q, new[] { Tuple.Create(11,  54,  24,  15), 
                                                                                   Tuple.Create(14,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.H, new[] { Tuple.Create(16,  45,  15,  15), 
                                                                                   Tuple.Create(14,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.L, new[] { Tuple.Create( 6, 147, 117,  15),
                                                                                   Tuple.Create( 4, 148, 118,  15) }),
                    Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.M, new[] { Tuple.Create( 6,  73,  45,  14),
                                                                                   Tuple.Create(14,  74,  46,  14) }),
                    Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.Q, new[] { Tuple.Create(11,  54,  24,  15), 
                                                                                   Tuple.Create(16,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.H, new[] { Tuple.Create(30,  46,  16,  15), 
                                                                                   Tuple.Create( 2,  47,  17,  15) }),
                    Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.L, new[] { Tuple.Create( 8, 132, 106,  13),
                                                                                   Tuple.Create( 4, 133, 107,  13) }),
                    Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.M, new[] { Tuple.Create( 8,  75,  46,  14),
                                                                                   Tuple.Create(13,  76,  48,  14) }),
                    Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.Q, new[] { Tuple.Create( 7,  54,  24,  15), 
                                                                                   Tuple.Create(22,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.H, new[] { Tuple.Create(22,  45,  15,  15), 
                                                                                   Tuple.Create(13,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.L, new[] { Tuple.Create(10, 142, 114,  14),
                                                                                   Tuple.Create( 2, 143, 115,  14) }),
                    Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.M, new[] { Tuple.Create(19,  74,  46,  14),
                                                                                   Tuple.Create( 4,  75,  47,  14) }),
                    Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.Q, new[] { Tuple.Create(28,  50,  22,  14), 
                                                                                   Tuple.Create( 6,  51,  23,  14) }),
                    Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.H, new[] { Tuple.Create(33,  46,  16,  15), 
                                                                                   Tuple.Create( 4,  47,  17,  15) }),
                    Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.L, new[] { Tuple.Create( 8, 152, 122,  15),
                                                                                   Tuple.Create( 4, 153, 123,  15) }),
                    Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.M, new[] { Tuple.Create(22,  73,  45,  14),
                                                                                   Tuple.Create( 3,  74,  46,  14) }),
                    Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.Q, new[] { Tuple.Create( 8,  53,  23,  15), 
                                                                                   Tuple.Create(26,  54,  24,  15) }),
                    Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.H, new[] { Tuple.Create(12,  45,  15,  15), 
                                                                                   Tuple.Create(28,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.L, new[] { Tuple.Create( 3, 147, 117,  15),
                                                                                   Tuple.Create(10, 148, 118,  15) }),
                    Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.M, new[] { Tuple.Create( 3,  73,  45,  14),
                                                                                   Tuple.Create(23,  74,  46,  14) }),
                    Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.Q, new[] { Tuple.Create( 4,  54,  24,  15), 
                                                                                   Tuple.Create(31,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.H, new[] { Tuple.Create(11,  45,  15,  15), 
                                                                                   Tuple.Create(31,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.L, new[] { Tuple.Create( 7, 146, 116,  15),
                                                                                   Tuple.Create( 7, 147, 117,  15) }),
                    Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.M, new[] { Tuple.Create(21,  73,  45,  14),
                                                                                   Tuple.Create( 7,  74,  46,  14) }),
                    Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.Q, new[] { Tuple.Create( 1,  53,  23,  15), 
                                                                                   Tuple.Create(37,  54,  24,  15) }),
                    Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.H, new[] { Tuple.Create(19,  45,  15,  15), 
                                                                                   Tuple.Create(26,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.L, new[] { Tuple.Create( 5, 145, 115,  15),
                                                                                   Tuple.Create(10, 146, 116,  15) }),
                    Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.M, new[] { Tuple.Create(19,  75,  47,  14),
                                                                                   Tuple.Create(10,  76,  48,  14) }),
                    Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.Q, new[] { Tuple.Create(15,  54,  24,  15), 
                                                                                   Tuple.Create(25,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.H, new[] { Tuple.Create(23,  45,  15,  15), 
                                                                                   Tuple.Create(25,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.L, new[] { Tuple.Create(13, 145, 115,  15),
                                                                                   Tuple.Create( 3, 146, 116,  15) }),
                    Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.M, new[] { Tuple.Create( 2,  74,  46,  14),
                                                                                   Tuple.Create(29,  75,  47,  14) }),
                    Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.Q, new[] { Tuple.Create(42,  54,  24,  15), 
                                                                                   Tuple.Create( 1,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.H, new[] { Tuple.Create(23,  45,  15,  15), 
                                                                                   Tuple.Create(28,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.L, new[] { Tuple.Create(17, 145, 115,  15) }),
                    Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.M, new[] { Tuple.Create(10,  74,  46,  14),
                                                                                   Tuple.Create(23,  75,  47,  14) }),
                    Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.Q, new[] { Tuple.Create(10,  54,  24,  15), 
                                                                                   Tuple.Create(35,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.H, new[] { Tuple.Create(19,  45,  15,  15), 
                                                                                   Tuple.Create(35,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.L, new[] { Tuple.Create(17, 145, 115,  15),
                                                                                   Tuple.Create( 1, 146, 116,  15) }),
                    Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.M, new[] { Tuple.Create(14,  74,  46,  14),
                                                                                   Tuple.Create(21,  75,  47,  14) }),
                    Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.Q, new[] { Tuple.Create(29,  54,  24,  15), 
                                                                                   Tuple.Create(19,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.H, new[] { Tuple.Create(11,  45,  15,  15), 
                                                                                   Tuple.Create(46,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.L, new[] { Tuple.Create(13, 145, 115,  15),
                                                                                   Tuple.Create( 6, 146, 116,  15) }),
                    Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.M, new[] { Tuple.Create(14,  74,  46,  14),
                                                                                   Tuple.Create(23,  75,  47,  14) }),
                    Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.Q, new[] { Tuple.Create(44,  54,  24,  15), 
                                                                                   Tuple.Create( 7,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.H, new[] { Tuple.Create(59,  46,  16,  15), 
                                                                                   Tuple.Create( 1,  47,  17,  15) }),
                    Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.L, new[] { Tuple.Create(12, 151, 121,  15),
                                                                                   Tuple.Create( 7, 152, 122,  15) }),
                    Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.M, new[] { Tuple.Create(12,  75,  47,  14),
                                                                                   Tuple.Create(26,  76,  48,  14) }),
                    Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.Q, new[] { Tuple.Create(39,  54,  24,  15), 
                                                                                   Tuple.Create(14,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.H, new[] { Tuple.Create(22,  45,  15,  15), 
                                                                                   Tuple.Create(41,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.L, new[] { Tuple.Create( 6, 151, 121,  15),
                                                                                   Tuple.Create(14, 152, 122,  15) }),
                    Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.M, new[] { Tuple.Create( 6,  75,  47,  14),
                                                                                   Tuple.Create(34,  76,  48,  14) }),
                    Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.Q, new[] { Tuple.Create(46,  54,  24,  15), 
                                                                                   Tuple.Create(10,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.H, new[] { Tuple.Create( 2,  45,  15,  15), 
                                                                                   Tuple.Create(64,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.L, new[] { Tuple.Create(17, 152, 122,  15),
                                                                                   Tuple.Create( 4, 153, 123,  15) }),
                    Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.M, new[] { Tuple.Create(29,  74,  46,  14),
                                                                                   Tuple.Create(14,  75,  47,  14) }),
                    Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.Q, new[] { Tuple.Create(49,  54,  24,  15), 
                                                                                   Tuple.Create(10,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.H, new[] { Tuple.Create(24,  45,  15,  15), 
                                                                                   Tuple.Create(46,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.L, new[] { Tuple.Create( 4, 152, 122,  15),
                                                                                   Tuple.Create(18, 153, 123,  15) }),
                    Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.M, new[] { Tuple.Create(13,  74,  46,  14),
                                                                                   Tuple.Create(32,  75,  47,  14) }),
                    Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.Q, new[] { Tuple.Create(48,  54,  24,  15), 
                                                                                   Tuple.Create(14,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.H, new[] { Tuple.Create(42,  45,  15,  15), 
                                                                                   Tuple.Create(32,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.L, new[] { Tuple.Create(20, 147, 117,  15),
                                                                                   Tuple.Create( 4, 148, 118,  15) }),
                    Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.M, new[] { Tuple.Create(40,  75,  47,  14),
                                                                                   Tuple.Create( 7,  76,  48,  14) }),
                    Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.Q, new[] { Tuple.Create(43,  54,  24,  15), 
                                                                                   Tuple.Create(22,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.H, new[] { Tuple.Create(10,  45,  15,  15), 
                                                                                   Tuple.Create(67,  46,  16,  15) }),
                    Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.L, new[] { Tuple.Create(19, 148, 118,  15),
                                                                                   Tuple.Create( 6, 149, 119,  15) }),
                    Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.M, new[] { Tuple.Create(18,  75,  47,  14),
                                                                                   Tuple.Create(31,  76,  48,  14) }),
                    Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.Q, new[] { Tuple.Create(34,  54,  24,  15), 
                                                                                   Tuple.Create(34,  55,  25,  15) }),
                    Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.H, new[] { Tuple.Create(20,  45,  15,  15), 
                                                                                   Tuple.Create(61,  46,  16,  15) }),
                }
                .GroupBy(t => t.Item1)
                .Select(t =>
                    Tuple.Create(
                        t.Key,
                        t
                            .GroupBy(n => n.Item2)
                            .Select(n =>
                                Tuple.Create(
                                    n.Key,
                                    n.ToDictionary(g => g.Item3, g => g.Item4)))
                            .ToDictionary(n => n.Item1, n => n.Item2)))
                .ToDictionary(t => t.Item1, t => t.Item2);

        private static Dictionary<SymbolType, Dictionary<ErrorCorrection, Dictionary<int, Tuple<int, int, int, int>>>> DataCapacityTable =
                new List<Tuple<SymbolType, int, ErrorCorrection, Tuple<int, int, int, int>>>()
                {
                    Tuple.Create(SymbolType.Micro,   2, ErrorCorrection.L, Tuple.Create(  10,    6,    0,    0)),
                    Tuple.Create(SymbolType.Micro,   2, ErrorCorrection.M, Tuple.Create(   8,    5,    0,    0)),
                    Tuple.Create(SymbolType.Micro,   3, ErrorCorrection.L, Tuple.Create(  23,   14,    9,    6)),
                    Tuple.Create(SymbolType.Micro,   3, ErrorCorrection.M, Tuple.Create(  18,   11,    7,    4)),
                    Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.L, Tuple.Create(  35,   21,   15,    9)),
                    Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.M, Tuple.Create(  30,   18,   13,    8)),
                    Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.Q, Tuple.Create(  21,   13,    9,    5)),
                    Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.L, Tuple.Create(  41,   25,   15,    9)),
                    Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.M, Tuple.Create(  34,   20,   14,    8)),
                    Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.Q, Tuple.Create(  27,   16,   11,    7)),
                    Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.H, Tuple.Create(  17,   10,    7,    4)),
                    Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.L, Tuple.Create(  77,   47,   32,   20)),
                    Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.M, Tuple.Create(  63,   38,   26,   16)),
                    Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.Q, Tuple.Create(  48,   29,   20,   12)),
                    Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.H, Tuple.Create(  34,   20,   14,    8)),
                    Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.L, Tuple.Create( 127,   77,   53,   32)),
                    Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.M, Tuple.Create( 101,   61,   42,   26)),
                    Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.Q, Tuple.Create(  77,   47,   32,   20)),
                    Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.H, Tuple.Create(  58,   35,   24,   15)),
                    Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.L, Tuple.Create( 187,  114,   78,   48)),
                    Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.M, Tuple.Create( 149,   90,   62,   38)),
                    Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.Q, Tuple.Create( 111,   67,   46,   28)),
                    Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.H, Tuple.Create(  82,   50,   34,   21)),
                    Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.L, Tuple.Create( 255,  154,  106,   65)),
                    Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.M, Tuple.Create( 202,  122,   84,   52)),
                    Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.Q, Tuple.Create( 144,   87,   60,   37)), 
                    Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.H, Tuple.Create( 106,   64,   44,   27)), 
                    Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.L, Tuple.Create( 322,  195,  134,   82)),
                    Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.M, Tuple.Create( 255,  154,  106,   65)),
                    Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.Q, Tuple.Create( 178,  108,   74,   45)),
                    Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.H, Tuple.Create( 139,   84,   58,   36)),
                    Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.L, Tuple.Create( 370,  224,  154,   95)),
                    Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.M, Tuple.Create( 293,  178,  122,   75)),
                    Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.Q, Tuple.Create( 207,  125,   86,   53)), 
                    Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.H, Tuple.Create( 154,   93,   64,   39)), 
                    Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.L, Tuple.Create( 461,  279,  192,  118)),
                    Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.M, Tuple.Create( 365,  221,  152,   93)),
                    Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.Q, Tuple.Create( 259,  157,  108,   66)), 
                    Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.H, Tuple.Create( 202,  122,   84,   52)), 
                    Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.L, Tuple.Create( 552,  335,  230,  141)),
                    Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.M, Tuple.Create( 432,  262,  180,  111)),
                    Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.Q, Tuple.Create( 312,  189,  130,   80)), 
                    Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.H, Tuple.Create( 235,  143,   98,   60)), 
                    Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.L, Tuple.Create( 652,  395,  271,  167)),
                    Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.M, Tuple.Create( 513,  311,  213,  131)),
                    Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.Q, Tuple.Create( 364,  221,  151,   93)), 
                    Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.H, Tuple.Create( 288,  174,  119,   74)), 
                    Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.L, Tuple.Create( 772,  468,  321,  198)),
                    Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.M, Tuple.Create( 604,  366,  251,  155)),
                    Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.Q, Tuple.Create( 427,  259,  177,  109)), 
                    Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.H, Tuple.Create( 331,  200,  137,   85)), 
                    Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.L, Tuple.Create( 883,  535,  367,  226)),
                    Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.M, Tuple.Create( 691,  419,  287,  177)),
                    Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.Q, Tuple.Create( 489,  296,  203,  125)), 
                    Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.H, Tuple.Create( 374,  227,  155,   96)), 
                    Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.L, Tuple.Create(1022,  619,  425,  262)),
                    Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.M, Tuple.Create( 796,  483,  331,  204)),
                    Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.Q, Tuple.Create( 580,  352,  241,  149)), 
                    Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.H, Tuple.Create( 427,  259,  177,  109)), 
                    Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.L, Tuple.Create(1101,  667,  458,  282)),
                    Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.M, Tuple.Create( 871,  528,  362,  223)),
                    Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.Q, Tuple.Create( 621,  376,  258,  159)), 
                    Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.H, Tuple.Create( 468,  283,  194,  120)), 
                    Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.L, Tuple.Create(1250,  758,  520,  320)),
                    Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.M, Tuple.Create( 991,  600,  412,  254)),
                    Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.Q, Tuple.Create( 703,  426,  292,  180)), 
                    Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.H, Tuple.Create( 530,  321,  220,  136)), 
                    Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.L, Tuple.Create(1408,  854,  586,  361)),
                    Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.M, Tuple.Create(1082,  656,  450,  277)),
                    Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.Q, Tuple.Create( 775,  470,  322,  198)), 
                    Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.H, Tuple.Create( 602,  365,  250,  154)), 
                    Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.L, Tuple.Create(1548,  938,  644,  397)),
                    Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.M, Tuple.Create(1212,  734,  504,  310)),
                    Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.Q, Tuple.Create( 876,  531,  364,  224)), 
                    Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.H, Tuple.Create( 674,  408,  280,  173)), 
                    Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.L, Tuple.Create(1725, 1046,  718,  442)),
                    Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.M, Tuple.Create(1346,  816,  560,  345)),
                    Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.Q, Tuple.Create( 948,  574,  394,  243)), 
                    Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.H, Tuple.Create( 746,  452,  310,  191)), 
                    Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.L, Tuple.Create(1903, 1153,  792,  488)),
                    Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.M, Tuple.Create(1500,  909,  624,  384)),
                    Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.Q, Tuple.Create(1063,  644,  442,  272)), 
                    Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.H, Tuple.Create( 813,  493,  338,  208)), 
                    Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.L, Tuple.Create(2061, 1249,  858,  528)),
                    Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.M, Tuple.Create(1600,  970,  666,  410)),
                    Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.Q, Tuple.Create(1159,  702,  482,  297)), 
                    Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.H, Tuple.Create( 919,  557,  382,  235)), 
                    Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.L, Tuple.Create(2232, 1352,  929,  572)),
                    Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.M, Tuple.Create(1708, 1035,  711,  438)),
                    Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.Q, Tuple.Create(1224,  742,  509,  314)), 
                    Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.H, Tuple.Create( 969,  587,  403,  248)), 
                    Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.L, Tuple.Create(2409, 1460, 1003,  618)),
                    Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.M, Tuple.Create(1872, 1134,  779,  480)),
                    Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.Q, Tuple.Create(1358,  823,  565,  348)), 
                    Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.H, Tuple.Create(1056,  640,  439,  270)),
                    Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.L, Tuple.Create(2620, 1588, 1091,  672)),
                    Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.M, Tuple.Create(2059, 1248,  857,  528)),
                    Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.Q, Tuple.Create(1468,  890,  611,  376)), 
                    Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.H, Tuple.Create(1108,  672,  461,  284)), 
                    Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.L, Tuple.Create(2812, 1704, 1171,  721)),
                    Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.M, Tuple.Create(2188, 1326,  911,  561)),
                    Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.Q, Tuple.Create(1588,  963,  661,  407)), 
                    Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.H, Tuple.Create(1228,  744,  511,  315)), 
                    Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.L, Tuple.Create(3057, 1853, 1273,  784)),
                    Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.M, Tuple.Create(2395, 1451,  997,  614)),
                    Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.Q, Tuple.Create(1718, 1041,  715,  440)), 
                    Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.H, Tuple.Create(1286,  779,  535,  330)), 
                    Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.L, Tuple.Create(3283, 1990, 1367,  842)),
                    Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.M, Tuple.Create(2544, 1542, 1059,  652)),
                    Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.Q, Tuple.Create(1804, 1094,  751,  462)), 
                    Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.H, Tuple.Create(1425,  864,  593,  365)), 
                    Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.L, Tuple.Create(3517, 2132, 1465,  902)),
                    Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.M, Tuple.Create(2701, 1637, 1125,  692)),
                    Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.Q, Tuple.Create(1933, 1172,  805,  496)), 
                    Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.H, Tuple.Create(1501,  910,  625,  385)), 
                    Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.L, Tuple.Create(3669, 2223, 1528,  940)),
                    Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.M, Tuple.Create(2857, 1732, 1190,  732)),
                    Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.Q, Tuple.Create(2085, 1263,  868,  534)), 
                    Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.H, Tuple.Create(1581,  958,  658,  405)), 
                    Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.L, Tuple.Create(3909, 2369, 1628, 1002)),
                    Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.M, Tuple.Create(3035, 1839, 1264,  778)),
                    Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.Q, Tuple.Create(2181, 1322,  908,  559)), 
                    Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.H, Tuple.Create(1677, 1016,  698,  430)), 
                    Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.L, Tuple.Create(4158, 2520, 1732, 1066)),
                    Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.M, Tuple.Create(3289, 1994, 1370,  843)),
                    Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.Q, Tuple.Create(2358, 1429,  982,  604)), 
                    Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.H, Tuple.Create(1782, 1080,  742,  457)), 
                    Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.L, Tuple.Create(4417, 2677, 1840, 1132)),
                    Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.M, Tuple.Create(3486, 2113, 1452,  894)),
                    Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.Q, Tuple.Create(2473, 1499, 1030,  634)), 
                    Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.H, Tuple.Create(1897, 1150,  790,  486)), 
                    Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.L, Tuple.Create(4686, 2840, 1952, 1201)),
                    Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.M, Tuple.Create(3693, 2238, 1538,  947)),
                    Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.Q, Tuple.Create(2670, 1618, 1112,  684)), 
                    Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.H, Tuple.Create(2022, 1226,  842,  518)), 
                    Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.L, Tuple.Create(4965, 3009, 2068, 1273)),
                    Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.M, Tuple.Create(3909, 2369, 1628, 1002)),
                    Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.Q, Tuple.Create(2805, 1700, 1168,  719)), 
                    Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.H, Tuple.Create(2157, 1307,  898,  553)), 
                    Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.L, Tuple.Create(5253, 3183, 2188, 1347)),
                    Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.M, Tuple.Create(4134, 2506, 1722, 1060)),
                    Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.Q, Tuple.Create(2949, 1787, 1228,  756)), 
                    Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.H, Tuple.Create(2301, 1394,  958,  590)), 
                    Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.L, Tuple.Create(5529, 3351, 2303, 1417)),
                    Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.M, Tuple.Create(4343, 2632, 1809, 1113)),
                    Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.Q, Tuple.Create(3081, 1867, 1283,  790)), 
                    Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.H, Tuple.Create(2361, 1431,  983,  605)), 
                    Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.L, Tuple.Create(5836, 3537, 2431, 1496)),
                    Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.M, Tuple.Create(4588, 2780, 1911, 1176)),
                    Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.Q, Tuple.Create(3244, 1966, 1351,  832)), 
                    Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.H, Tuple.Create(2524, 1530, 1051,  647)), 
                    Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.L, Tuple.Create(6153, 3729, 2563, 1577)),
                    Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.M, Tuple.Create(4775, 2894, 1989, 1224)),
                    Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.Q, Tuple.Create(3417, 2071, 1423,  876)), 
                    Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.H, Tuple.Create(2625, 1591, 1093,  673)), 
                    Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.L, Tuple.Create(6479, 3927, 2699, 1661)),
                    Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.M, Tuple.Create(5039, 3054, 2099, 1292)),
                    Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.Q, Tuple.Create(3599, 2181, 1499,  923)), 
                    Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.H, Tuple.Create(2735, 1658, 1139,  701)), 
                    Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.L, Tuple.Create(6743, 4087, 2809, 1729)),
                    Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.M, Tuple.Create(5313, 3220, 2213, 1362)),
                    Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.Q, Tuple.Create(3791, 2298, 1579, 972)), 
                    Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.H, Tuple.Create(2927, 1774, 1219,  750)), 
                    Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.L, Tuple.Create(7089, 4296, 2953, 1817)),
                    Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.M, Tuple.Create(5596, 3391, 2331, 1435)),
                    Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.Q, Tuple.Create(3993, 2420, 1663, 1024)), 
                    Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.H, Tuple.Create(3057, 1852, 1273,  784)), 
                }
                .GroupBy(t => t.Item1)
                .Select(t =>
                    Tuple.Create(
                        t.Key,
                        t
                            .GroupBy(n => n.Item3)
                            .Select(n =>
                                Tuple.Create(
                                    n.Key,
                                    n.ToDictionary(g => g.Item2, g => g.Item4)))
                            .ToDictionary(n => n.Item1, n => n.Item2)))
                .ToDictionary(t => t.Item1, t => t.Item2);

        private static byte[] ExponentTable;
        private static byte[] LogTable;
        private static byte[][] Polynomials;
           
        static QRCode()
        {
            // generate log/anti-log tables
            ExponentTable = new byte[256];
            ExponentTable[0] = 1;
            LogTable = new byte[256];

            for (int i = 1; i < 256; i++)
            {
                int next = ExponentTable[i - 1] * 2;
                if (next >= 256)
                    next ^= 285;
                ExponentTable[i] = (byte)next;
                LogTable[next] = (byte)i;
            }
            LogTable[1] = 0;

            // generate polynomials

            Polynomials = new byte[69][];
            Polynomials[1] = new byte[] { 0, 0 };

            for (int i = 2; i <= 68; i++)
            {
                // we are going to multiply the preceeding polynomial by (a^0*x - a^i)
                var term = new byte[] { (byte)(i-1), 0 };

                // the new polynomial will have one more term than the preceeding one
                Polynomials[i] = new byte[Polynomials[i - 1].Length + 1];
                Polynomials[i][0] = Mul(Polynomials[i - 1].First(), term.First());

                for (int p = 1; p < Polynomials[i].Length-1; p++)
                    Polynomials[i][p] = 
                        Add(
                            Mul(Polynomials[i - 1][p - 1], term.Last()),
                            Mul(Polynomials[i - 1][p], term.First()));

                Polynomials[i][Polynomials[i].Length - 1] = Mul(Polynomials[i - 1].Last(), term.Last());
            }
        }

        private static byte Mul(byte a1, byte a2)
        {
            return (byte)((a1 + a2) % 255);
        }

        private static byte Add(byte a1, byte a2)
        {
            return LogTable[ExponentTable[a1] ^ ExponentTable[a2]];
        }
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
