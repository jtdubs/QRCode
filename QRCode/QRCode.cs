using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

namespace QRCode
{
    /// <summary>
    /// Type of an individual module (pixel) of a QR symbol.
    /// </summary>
    public enum ModuleType
    {
        Light,
        Dark
    }

    /// <summary>
    /// QR encoding modes
    /// </summary>
    public enum Mode
    {
        ECI = 0,
        Numeric = 1,
        AlphaNumeric = 2,
        Byte = 3,
        Kanji = 4,
        StructuredAppend = 5,
        FNC1_FirstPosition = 6,
        FNC1_SecondPosition = 7,
        Terminator = 8
    }

    /// <summary>
    /// QR symbol types
    /// </summary>
    public enum SymbolType
    {
        Micro,
        Normal
    }

    /// <summary>
    /// QR error correction modes
    /// </summary>
    public enum ErrorCorrection
    {
        [Description("Error-Detection Only")]
        None = 0,
        [Description("L (7%)")]
        L = 1,
        [Description("M (15%)")]
        M = 2,
        [Description("Q (25%)")]
        Q = 3,
        [Description("H (30%)")]
        H = 4
    }

    /// <summary>
    /// Helpers functions for BitArrays
    /// </summary>
    public static class BitArrayHelpers
    {
        public static string ToBitString(this BitArray bits)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < bits.Length; i++)
            {
                sb.Append(bits[i] ? "1" : "0");
                if (i % 8 == 7)
                    sb.Append(" ");
            }

            return sb.ToString();
        }

        public static byte[] ToByteArray(this BitArray bits)
        {
            byte[] bytes = new byte[(bits.Length - 1) / 8 + 1];
            for (int b = 0; b < bits.Length; b++)
                if (bits[b])
                    bytes[b / 8] |= (byte)(0x80 >> (b % 8));
            return bytes;
        }

        public static BitArray ToBitArray(this byte[] bytes)
        {
            var b = new BitArray(8 * bytes.Length, false);

            for (int i = 0; i < b.Length; i++)
                if ((bytes[i / 8] & (0x80 >> (i % 8))) != 0)
                    b[i] = true;

            return b;
        }

        public static BitArray ToBitArray(this int x, int bits)
        {
            var b = new BitArray(bits, false);
            for (int i = 0; i < bits; i++)
                if ((x & ((1 << (bits - 1)) >> i)) != 0)
                    b[i] = true;
            return b;
        }
    }

    /// <summary>
    /// A QR symbol
    /// </summary>
    public class QRCode
    {
        #region Construction
        /// <summary>
        /// Create a QR symbol that represents the supplied `data'.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="type"></param>
        /// <param name="errorCorrection"></param>
        public QRCode(string data)
            : this(data, ErrorCorrection.M, false)
        {
        }

        /// <summary>
        /// Create a QR symbol that represents the supplied `data' with the indicated minimum level of error correction.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="type"></param>
        /// <param name="errorCorrection"></param>
        public QRCode(string data, ErrorCorrection minimumErrorCorrection)
            : this(data, minimumErrorCorrection, false)
        {
        }

        /// <summary>
        /// Create a QR symbol that represents the supplied `data' with the indicated minimum level of error correction.
        /// </summary>
        public QRCode(string data, ErrorCorrection minimumErrorCorrection, bool allowMicroCodes)
        {
            var mode = ChooseParameters(data, minimumErrorCorrection, allowMicroCodes);
            var codeWords = CreateCodeWords(data, mode);
            var bits = AddErrorCorrection(codeWords);
            Reserve();
            Fill(bits);
            var mask = Mask();
            AddFormatInformation(mask);
            AddVersionInformation();
        }
        #endregion

        #region External Interface
        /// <summary>
        /// Type of QR symbol (normal or micro)
        /// </summary>
        public SymbolType Type { get; private set; }

        /// <summary>
        /// Version of QR symbol (1-5 or 1-40, depending on type)
        /// </summary>
        public int Version { get; private set; }

        /// <summary>
        /// Level of error correction in this symbol
        /// </summary>
        public ErrorCorrection ErrorCorrection { get; private set; }

        /// <summary>
        /// A textual description of a QR code's metadata
        /// </summary>
        public string Description
        {
            get
            {
                switch (Type)
                {
                    case SymbolType.Micro:
                        if (Version == 1)
                            return String.Format("QR M{0}", Version);
                        else
                            return String.Format("QR M{0}-{1}", Version, ErrorCorrection);
                    case SymbolType.Normal:
                        return String.Format("QR {0}-{1}", Version, ErrorCorrection);
                }

                throw new InvalidOperationException();
            }
        }

        /// <summary>
        /// Save the QR code as an image at the following scale.
        /// </summary>
        /// <param name="path">Path of image file to create.</param>
        /// <param name="scale">Size of a module, in pixels.</param>
        public void Save(string imagePath, int scale)
        {
            using (Bitmap b = ToBitmap(scale))
            {
                b.Save(imagePath, ImageFormat.Png);
            }
        }

        /// <summary>
        /// Generate a bitmap of this QR code at the following scale.
        /// </summary>
        /// <param name="scale"></param>
        /// <returns></returns>
        public Bitmap ToBitmap(int scale)
        {
            Bitmap b = new Bitmap(dim * scale, dim * scale);

            using (Graphics g = Graphics.FromImage(b))
            {
                Render(g, scale);
            }

            return b;
        }

        /// <summary>
        /// Render this bitmap to the supplied Graphics object at the indicated scale.
        /// </summary>
        /// <param name="scale"></param>
        /// <returns></returns>
        public void Render(Graphics g, int scale)
        {
            var brush = new SolidBrush(Color.Black);

            g.Clear(Color.White);

            for (int x = 0; x < dim; x++)
            {
                for (int y = 0; y < dim; y++)
                {
                    if (Get(x, y) == ModuleType.Dark)
                    {
                        g.FillRectangle(brush, x * scale, y * scale, scale, scale);
                    }
                }
            }
        }
        #endregion

        #region Steps
        /// <summary>
        /// Choose suitable values for Type, Version, ErrorCorrection and Mode.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private Mode ChooseParameters(string data, ErrorCorrection minimumErrorCorrection, bool allowMicroCodes)
        {
            // get list of error correction modes at least as good as the user-specified one
            var allowedErrorCorrectionModes = new ErrorCorrection[]
            {
                ErrorCorrection.None,
                ErrorCorrection.L,
                ErrorCorrection.M,
                ErrorCorrection.Q,
                ErrorCorrection.H,
            }.SkipWhile(e => e != minimumErrorCorrection).ToList();

            // get the tightest-fit encoding mode
            Mode tightestMode;
            if (data.All(c => Char.IsDigit(c)))
                tightestMode = Mode.Numeric;
            else if (data.All(c => AlphaNumericTable.ContainsKey(c)))
                tightestMode = Mode.AlphaNumeric;
            else
                tightestMode = Mode.Byte;

            // get list of allowed encoding modes
            var allowedModes = new Mode[]
            {
                Mode.Numeric,
                Mode.AlphaNumeric,
                Mode.Byte
            }.SkipWhile(m => m != tightestMode).ToList();

            // get list of possible types
            List<Tuple<SymbolType, byte>> possibleTypes =
                allowMicroCodes
                ? Enumerable.Concat(
                        Enumerable.Range(1, 4).Select(i => Tuple.Create(SymbolType.Micro, (byte)i)),
                        Enumerable.Range(1, 40).Select(i => Tuple.Create(SymbolType.Normal, (byte)i))).ToList()
                : Enumerable.Range(1, 40).Select(i => Tuple.Create(SymbolType.Normal, (byte)i)).ToList();

            // for each type in ascending order of size
            foreach (var p in possibleTypes)
            {
                // for each error correction level from most to least
                foreach (var e in allowedErrorCorrectionModes.Intersect(GetAvailableErrorCorrectionLevels(p.Item1, p.Item2)).Reverse())
                {
                    // lookup the data capacity
                    var capacityEntry = DataCapacityTable.First(f => f.Item1 == p.Item1 && f.Item2 == p.Item2 && f.Item3 == e).Item4;

                    // for each encoding mode from tightest to loosest
                    foreach (var m in allowedModes.Intersect(GetAvailableModes(p.Item1, p.Item2)))
                    {
                        int capacity = 0;

                        switch (m)
                        {
                            case Mode.Numeric: capacity = capacityEntry.Item2; break;
                            case Mode.AlphaNumeric: capacity = capacityEntry.Item3; break;
                            case Mode.Byte: capacity = capacityEntry.Item4; break;
                            default: capacity = 0; break;
                        }

                        // if there is enough room, we've found our solution
                        if (capacity >= data.Length)
                        {
                            Type = p.Item1;
                            Version = p.Item2;
                            ErrorCorrection = e;
                            return m;
                        }
                    }
                }
            }

            throw new InvalidOperationException("no suitable parameters found");
        }

        /// <summary>
        /// Encode the data in the following mode, pad, and return final code words.
        /// </summary>
        /// <param name="data">The data to encode.</param>
        /// <returns>The fully-encoded data.</returns>
        private byte[] CreateCodeWords(string data, Mode mode)
        {
            #region Code word creation
            // encode data as series of bit arrays
            List<BitArray> bits = new List<BitArray>();

            // add mode indicator
            bits.Add(EncodeMode(mode));

            // add character count
            bits.Add(EncodeCharacterCount(mode, data.Length));

            // perform mode-specific data encoding
            switch (mode)
            {
                case Mode.Byte:
                    {
                        // retrieve UTF8 encoding of data
                        bits.Add(Encoding.UTF8.GetBytes(data).ToBitArray());
                    }
                    break;

                case Mode.Numeric:
                    {
                        int idx;

                        // for every triple of digits
                        for (idx = 0; idx < data.Length - 2; idx += 3)
                        {
                            // encode them as a 3-digit decimal number
                            int x = AlphaNumericTable[data[idx]] * 100 + AlphaNumericTable[data[idx + 1]] * 10 + AlphaNumericTable[data[idx + 2]];
                            bits.Add(x.ToBitArray(10));
                        }

                        // if there is a remaining pair of digits
                        if (idx < data.Length - 1)
                        {
                            // encode them as a 2-digit decimal number
                            int x = AlphaNumericTable[data[idx]] * 10 + AlphaNumericTable[data[idx + 1]];
                            idx += 2;
                            bits.Add(x.ToBitArray(7));
                        }

                        // if there is a remaining digit
                        if (idx < data.Length)
                        {
                            // encode it as a decimal number
                            int x = AlphaNumericTable[data[idx]];
                            idx += 1;
                            bits.Add(x.ToBitArray(4));
                        }
                    }
                    break;

                case Mode.AlphaNumeric:
                    {
                        int idx;

                        // for every pair of characters
                        for (idx = 0; idx < data.Length - 1; idx += 2)
                        {
                            // encode them as a single number
                            int x = AlphaNumericTable[data[idx]] * 45 + AlphaNumericTable[data[idx + 1]];
                            bits.Add(x.ToBitArray(11));
                        }

                        // if there is a remaining character
                        if (idx < data.Length)
                        {
                            // encode it as a number
                            int x = AlphaNumericTable[data[idx]];
                            bits.Add(x.ToBitArray(6));
                        }
                    }
                    break;
            }

            // add the terminator mode marker
            bits.Add(EncodeMode(Mode.Terminator));

            // calculate the bitstream's total length, in bits
            int bitstreamLength = bits.Sum(b => b.Length);

            // check the full capacity of symbol, in bits
            int capacity = DataCapacityTable.First(f => f.Item1 == Type && f.Item2 == Version && f.Item3 == ErrorCorrection).Item4.Item1 * 8;

            // M1 and M3 are actually shorter by 1 nibble
            if (Type == SymbolType.Micro && (Version == 3 || Version == 1))
                capacity -= 4;

            // pad the bitstream to the nearest octet boundary with zeroes
            if (bitstreamLength < capacity && bitstreamLength % 8 != 0)
            {
                int paddingLength = Math.Min(8 - (bitstreamLength % 8), capacity - bitstreamLength);
                bits.Add(new BitArray(paddingLength));
                bitstreamLength += paddingLength;
            }

            // fill the bitstream with pad codewords
            byte[] padCodewords = new byte[] { 0x37, 0x88 };
            int padIndex = 0;
            while (bitstreamLength < (capacity - 4))
            {
                bits.Add(new BitArray(new byte[] { padCodewords[padIndex] }));
                bitstreamLength += 8;
                padIndex = (padIndex + 1) % 2;
            }

            // fill the last nibble with zeroes (only necessary for M1 and M3)
            if (bitstreamLength < capacity)
            {
                bits.Add(new BitArray(4));
                bitstreamLength += 4;
            }

            // flatten list of bitarrays into a single bool[]
            bool[] flattenedBits = new bool[bitstreamLength];
            int bitIndex = 0;
            foreach (var b in bits)
            {
                b.CopyTo(flattenedBits, bitIndex);
                bitIndex += b.Length;
            }

            return new BitArray(flattenedBits).ToByteArray();
        }

        /// <summary>
        /// Generate error correction words and interleave with code words.
        /// </summary>
        /// <param name="codeWords"></param>
        /// <returns></returns>
        private BitArray AddErrorCorrection(byte[] codeWords)
        {
            List<byte[]> dataBlocks = new List<byte[]>();
            List<byte[]> eccBlocks = new List<byte[]>();

            // generate error correction words
            var ecc = ErrorCorrectionTable.First(f => f.Item1 == Type && f.Item2 == Version && f.Item3 == ErrorCorrection).Item4;
            int dataIndex = 0;

            // for each collection of blocks that are needed
            foreach (var e in ecc)
            {
                // lookup number of data words and error words in this block
                int dataWords = e.Item3;
                int errorWords = e.Item2 - e.Item3;

                // retrieve the appropriate polynomial for the desired error word count
                var poly = Polynomials[errorWords].ToArray();

                // for each needed block
                for (int b = 0; b < e.Item1; b++)
                {
                    // add the block's data to the final list
                    dataBlocks.Add(codeWords.Skip(dataIndex).Take(dataWords).ToArray());
                    dataIndex += dataWords;

                    // pad the block with zeroes
                    var temp = Enumerable.Concat(dataBlocks.Last(), Enumerable.Repeat((byte)0, errorWords)).ToArray();

                    // perform polynomial division to calculate error block
                    for (int start = 0; start < dataWords; start++)
                    {
                        byte pow = LogTable[temp[start]];
                        for (int i = 0; i < poly.Length; i++)
                            temp[i + start] ^= ExponentTable[Mul(poly[i], pow)];
                    }

                    // add error block to the final list
                    eccBlocks.Add(temp.Skip(dataWords).ToArray());
                }
            }
            #endregion

            // generate final data sequence
            byte[] sequence = new byte[dataBlocks.Sum(b => b.Length) + eccBlocks.Sum(b => b.Length)];
            int finalIndex = 0;

            // interleave the data blocks
            for (int i = 0; i < dataBlocks.Max(b => b.Length); i++)
                foreach (var b in dataBlocks.Where(b => b.Length > i))
                    sequence[finalIndex++] = b[i];

            // interleave the error blocks
            for (int i = 0; i < eccBlocks.Max(b => b.Length); i++)
                foreach (var b in eccBlocks.Where(b => b.Length > i))
                    sequence[finalIndex++] = b[i];

            return sequence.ToBitArray();
        }

        /// <summary>
        /// Perform the following steps
        /// - Draw finder patterns
        /// - Draw alignment patterns
        /// - Draw timing lines
        /// - Reserve space for version and format information
        /// - Mark remaining space as "free" for data
        /// </summary>
        private void Reserve()
        {
            dim = GetSymbolDimension();

            // initialize to a full symbol of unaccessed, light modules
            freeMask = new bool[dim, dim];
            accessCount = new int[dim, dim];
            modules = new ModuleType[dim, dim];
            for (int x = 0; x < dim; x++)
            {
                for (int y = 0; y < dim; y++)
                {
                    modules[x, y] = ModuleType.Light;
                    accessCount[x, y] = 0;
                    freeMask[x, y] = true;
                }
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

            // draw top-left finder pattern
            DrawFinderPattern(3, 3);
            // and border
            DrawHLine(0, 7, 8, ModuleType.Light);
            DrawVLine(7, 0, 7, ModuleType.Light);

            switch (Type)
            {
                case SymbolType.Micro:
                    // draw top-left finder pattern's format area
                    DrawHLine(1, 8, 8, ModuleType.Light);
                    DrawVLine(8, 1, 7, ModuleType.Light);

                    // draw timing lines
                    DrawTimingHLine(8, 0, dim - 8);
                    DrawTimingVLine(0, 8, dim - 8);
                    break;

                case SymbolType.Normal:
                    // draw top-left finder pattern's format area
                    DrawHLine(0, 8, 9, ModuleType.Light);
                    DrawVLine(8, 0, 8, ModuleType.Light);

                    // draw top-right finder pattern
                    DrawFinderPattern(dim - 4, 3);
                    // and border
                    DrawHLine(dim - 8, 7, 8, ModuleType.Light);
                    DrawVLine(dim - 8, 0, 7, ModuleType.Light);
                    // and format area
                    DrawHLine(dim - 8, 8, 8, ModuleType.Light);

                    // draw bottom-left finder pattern
                    DrawFinderPattern(3, dim - 4);
                    // and border
                    DrawHLine(0, dim - 8, 8, ModuleType.Light);
                    DrawVLine(7, dim - 7, 7, ModuleType.Light);
                    // and format area
                    DrawVLine(8, dim - 7, 7, ModuleType.Light);
                    // and dark module
                    Set(8, dim - 8, ModuleType.Dark);

                    // draw timing lines
                    DrawTimingHLine(8, 6, dim - 8 - 8);
                    DrawTimingVLine(6, 8, dim - 8 - 8);

                    if (Version >= 7)
                    {
                        // reserve version information areas
                        FillRect(0, dim - 11, 6, 3, ModuleType.Light);
                        FillRect(dim - 11, 0, 3, 6, ModuleType.Light);
                    }
                    break;
            }

            // mark non-accessed cells as free, accessed cells as reserved
            CreateFreeMask();
        }

        /// <summary>
        /// Populate the "free" modules with data.
        /// </summary>
        /// <param name="bits"></param>
        private void Fill(BitArray bits)
        {
            // start with bit 0, moving up
            int idx = 0;
            bool up = true;

            int minX = Type == SymbolType.Normal ? 0 : 1;
            int minY = Type == SymbolType.Normal ? 0 : 1;

            int timingX = Type == SymbolType.Normal ? 6 : 0;
            int timingY = Type == SymbolType.Normal ? 6 : 0;

            // from right-to-left
            for (int x = dim - 1; x >= minX; x -= 2)
            {
                // skip over the vertical timing line
                if (x == timingX)
                    x--;

                // in the indicated direction
                for (int y = (up ? dim - 1 : minY); y >= minY && y < dim; y += (up ? -1 : 1))
                {
                    // for each horizontal pair of modules
                    for (int dx = 0; dx > -2; dx--)
                    {
                        // if the module is free (not reserved)
                        if (IsFree(x + dx, y))
                        {
                            // if data remains to be written
                            if (idx < bits.Length)
                            {
                                // write the next bit
                                Set(x + dx, y, bits[idx] ? ModuleType.Dark : ModuleType.Light);
                            }
                            else
                            {
                                // pad with light cells
                                Set(x + dx, y, ModuleType.Light);
                            }

                            // advance to the next bit
                            idx++;
                        }
                    }
                }

                // reverse directions
                up = !up;
            }
        }

        /// <summary>
        /// Identify and apply the best mask
        /// </summary>
        /// <returns></returns>
        private byte Mask()
        {
            List<Tuple<byte, byte, Func<int, int, bool>>> masks = null;

            // determine which mask types are applicable
            switch (Type)
            {
                case SymbolType.Micro:
                    masks = DataMaskTable.Where(m => m.Item2 != 255).ToList();
                    break;

                case SymbolType.Normal:
                    masks = DataMaskTable.ToList();
                    break;
            }

            // evaluate all the maks
            var results = masks.Select(m => Tuple.Create(m, EvaluateMask(m.Item3)));

            // choose a winner
            Tuple<byte, byte, Func<int, int, bool>> winner;
            if (Type == SymbolType.Normal)
                winner = results.OrderBy(t => t.Item2).First().Item1; // lowest penalty wins
            else
                winner = results.OrderBy(t => t.Item2).Last().Item1; // highest score wins

            // apply the winner
            Apply(winner.Item3);

            // return the winner's ID
            return Type == SymbolType.Normal ? winner.Item1 : winner.Item2;
        }

        /// <summary>
        /// Write the format information (version and mask id)
        /// </summary>
        /// <param name="maskID"></param>
        private void AddFormatInformation(byte maskID)
        {
            if (Type == SymbolType.Normal)
            {
                var bits = NormalFormatStrings.First(f => f.Item1 == ErrorCorrection && f.Item2 == maskID).Item3;

                // add format information around top-left finder pattern
                Set(8, 0, bits[14] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 1, bits[13] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 2, bits[12] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 3, bits[11] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 4, bits[10] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 5, bits[9] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 7, bits[8] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 8, bits[7] ? ModuleType.Dark : ModuleType.Light);
                Set(7, 8, bits[6] ? ModuleType.Dark : ModuleType.Light);
                Set(5, 8, bits[5] ? ModuleType.Dark : ModuleType.Light);
                Set(4, 8, bits[4] ? ModuleType.Dark : ModuleType.Light);
                Set(3, 8, bits[3] ? ModuleType.Dark : ModuleType.Light);
                Set(2, 8, bits[2] ? ModuleType.Dark : ModuleType.Light);
                Set(1, 8, bits[1] ? ModuleType.Dark : ModuleType.Light);
                Set(0, 8, bits[0] ? ModuleType.Dark : ModuleType.Light);

                // add format information around top-right finder pattern
                Set(dim - 1, 8, bits[14] ? ModuleType.Dark : ModuleType.Light);
                Set(dim - 2, 8, bits[13] ? ModuleType.Dark : ModuleType.Light);
                Set(dim - 3, 8, bits[12] ? ModuleType.Dark : ModuleType.Light);
                Set(dim - 4, 8, bits[11] ? ModuleType.Dark : ModuleType.Light);
                Set(dim - 5, 8, bits[10] ? ModuleType.Dark : ModuleType.Light);
                Set(dim - 6, 8, bits[9] ? ModuleType.Dark : ModuleType.Light);
                Set(dim - 7, 8, bits[8] ? ModuleType.Dark : ModuleType.Light);
                Set(dim - 8, 8, bits[7] ? ModuleType.Dark : ModuleType.Light);

                // add format information around bottom-left finder pattern
                Set(8, dim - 7, bits[6] ? ModuleType.Dark : ModuleType.Light);
                Set(8, dim - 6, bits[5] ? ModuleType.Dark : ModuleType.Light);
                Set(8, dim - 5, bits[4] ? ModuleType.Dark : ModuleType.Light);
                Set(8, dim - 4, bits[3] ? ModuleType.Dark : ModuleType.Light);
                Set(8, dim - 3, bits[2] ? ModuleType.Dark : ModuleType.Light);
                Set(8, dim - 2, bits[1] ? ModuleType.Dark : ModuleType.Light);
                Set(8, dim - 1, bits[0] ? ModuleType.Dark : ModuleType.Light);
            }
            else
            {
                var bits = MicroFormatStrings.First(f => f.Item1 == Version && f.Item2 == ErrorCorrection && f.Item3 == maskID).Item4;

                // add format information around top-left finder pattern
                Set(8, 1, bits[14] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 2, bits[13] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 3, bits[12] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 4, bits[11] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 5, bits[10] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 6, bits[9] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 7, bits[8] ? ModuleType.Dark : ModuleType.Light);
                Set(8, 8, bits[7] ? ModuleType.Dark : ModuleType.Light);
                Set(7, 8, bits[6] ? ModuleType.Dark : ModuleType.Light);
                Set(6, 8, bits[5] ? ModuleType.Dark : ModuleType.Light);
                Set(5, 8, bits[4] ? ModuleType.Dark : ModuleType.Light);
                Set(4, 8, bits[3] ? ModuleType.Dark : ModuleType.Light);
                Set(3, 8, bits[2] ? ModuleType.Dark : ModuleType.Light);
                Set(2, 8, bits[1] ? ModuleType.Dark : ModuleType.Light);
                Set(1, 8, bits[0] ? ModuleType.Dark : ModuleType.Light);
            }
        }

        /// <summary>
        /// Write the version information
        /// </summary>
        private void AddVersionInformation()
        {
            if (Type == SymbolType.Micro || Version < 7)
                return;

            var bits = VersionStrings[Version];

            // write top-right block
            var idx = 17;
            for (int y = 0; y < 6; y++)
                for (int x = dim - 11; x < dim - 8; x++)
                    Set(x, y, bits[idx--] ? ModuleType.Dark : ModuleType.Light);

            // write bottom-left block
            idx = 17;
            for (int x = 0; x < 6; x++)
                for (int y = dim - 11; y < dim - 8; y++)
                    Set(x, y, bits[idx--] ? ModuleType.Dark : ModuleType.Light);
        }
        #endregion

        #region Drawing Helpers
        private void DrawFinderPattern(int centerX, int centerY)
        {
            DrawRect(centerX - 3, centerY - 3, 7, 7, ModuleType.Dark);
            DrawRect(centerX - 2, centerY - 2, 5, 5, ModuleType.Light);
            FillRect(centerX - 1, centerY - 1, 3, 3, ModuleType.Dark);
        }

        private void DrawAlignmentPattern(int centerX, int centerY)
        {
            DrawRect(centerX - 2, centerY - 2, 5, 5, ModuleType.Dark);
            DrawRect(centerX - 1, centerY - 1, 3, 3, ModuleType.Light);
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
            for (int dx = 0; dx < length; dx++)
                Set(x + dx, y, ((x + dx) % 2 == 0) ? ModuleType.Dark : ModuleType.Light);
        }

        private void DrawTimingVLine(int x, int y, int length)
        {
            for (int dy = 0; dy < length; dy++)
                Set(x, y + dy, ((y + dy) % 2 == 0) ? ModuleType.Dark : ModuleType.Light);
        }

        private void Set(int x, int y, ModuleType type)
        {
            modules[x, y] = type;
            accessCount[x, y]++;
        }

        private ModuleType Get(int x, int y)
        {
            return modules[x, y];
        }

        private void CreateFreeMask()
        {
            for (int x = 0; x < dim; x++)
                for (int y = 0; y < dim; y++)
                    freeMask[x, y] = accessCount[x, y] == 0;
        }

        private bool IsFree(int x, int y)
        {
            return freeMask[x, y];
        }
        #endregion

        #region Masking Helpers
        private int EvaluateMask(Func<int, int, bool> mask)
        {
            // apply the mask
            Apply(mask);

            try
            {
                if (Type == SymbolType.Normal)
                    return EvaluateNormalMask();
                else
                    return EvaluateMicroMask();
            }
            finally
            {
                // undo the mask
                Apply(mask);
            }
        }

        private void Apply(Func<int, int, bool> mask)
        {
            for (int x = 0; x < dim; x++)
            {
                for (int y = 0; y < dim; y++)
                {
                    if (IsFree(x, y) && mask(y, x))
                    {
                        Set(x, y, Get(x, y) == ModuleType.Dark ? ModuleType.Light : ModuleType.Dark);
                    }
                }
            }
        }

        private int EvaluateMicroMask()
        {
            int darkCount1 = Enumerable.Range(1, dim - 2).Count(x => Get(x, dim - 1) == ModuleType.Dark);
            int darkCount2 = Enumerable.Range(1, dim - 2).Count(y => Get(dim - 1, y) == ModuleType.Dark);

            return Math.Min(darkCount1, darkCount2) * 16 + Math.Max(darkCount1, darkCount2);
        }

        private int EvaluateNormalMask()
        {
            int penalty = 0;

            // horizontal adjacency penalties
            for (int y = 0; y < dim; y++)
            {
                ModuleType last = Get(0, y);
                int count = 1;

                for (int x = 1; x < dim; x++)
                {
                    var m = Get(x, y);
                    if (m == last)
                    {
                        count++;
                    }
                    else
                    {
                        if (count >= 5)
                            penalty += count - 2;

                        last = m;
                        count = 1;
                    }
                }

                if (count >= 5)
                    penalty += count - 2;
            }

            // vertical adjacency penalties
            for (int x = 0; x < dim; x++)
            {
                ModuleType last = Get(x, 0);
                int count = 1;

                for (int y = 1; y < dim; y++)
                {
                    var m = Get(x, y);
                    if (m == last)
                    {
                        count++;
                    }
                    else
                    {
                        if (count >= 5)
                            penalty += count - 2;

                        last = m;
                        count = 1;
                    }
                }

                if (count >= 5)
                    penalty += count - 2;
            }

            // block penalties
            for (int x = 0; x < dim - 1; x++)
            {
                for (int y = 0; y < dim - 1; y++)
                {
                    var m = Get(x, y);

                    if (m == Get(x + 1, y) && m == Get(x, y + 1) && m == Get(x + 1, y + 1))
                        penalty += 3;
                }
            }

            // horizontal finder pattern penalties
            for (int y = 0; y < dim; y++)
            {
                for (int x = 0; x < dim - 11; x++)
                {
                    if (Get(x + 0, y) == ModuleType.Dark &&
                        Get(x + 1, y) == ModuleType.Light &&
                        Get(x + 2, y) == ModuleType.Dark &&
                        Get(x + 3, y) == ModuleType.Dark &&
                        Get(x + 4, y) == ModuleType.Dark &&
                        Get(x + 5, y) == ModuleType.Light &&
                        Get(x + 6, y) == ModuleType.Dark &&
                        Get(x + 7, y) == ModuleType.Light &&
                        Get(x + 8, y) == ModuleType.Light &&
                        Get(x + 9, y) == ModuleType.Light &&
                        Get(x + 10, y) == ModuleType.Light)
                        penalty += 40;

                    if (Get(x + 0, y) == ModuleType.Light &&
                        Get(x + 1, y) == ModuleType.Light &&
                        Get(x + 2, y) == ModuleType.Light &&
                        Get(x + 3, y) == ModuleType.Light &&
                        Get(x + 4, y) == ModuleType.Dark &&
                        Get(x + 5, y) == ModuleType.Light &&
                        Get(x + 6, y) == ModuleType.Dark &&
                        Get(x + 7, y) == ModuleType.Dark &&
                        Get(x + 8, y) == ModuleType.Dark &&
                        Get(x + 9, y) == ModuleType.Light &&
                        Get(x + 10, y) == ModuleType.Dark)
                        penalty += 40;
                }
            }

            // vertical finder pattern penalties
            for (int x = 0; x < dim; x++)
            {
                for (int y = 0; y < dim - 11; y++)
                {
                    if (Get(x, y + 0) == ModuleType.Dark &&
                        Get(x, y + 1) == ModuleType.Light &&
                        Get(x, y + 2) == ModuleType.Dark &&
                        Get(x, y + 3) == ModuleType.Dark &&
                        Get(x, y + 4) == ModuleType.Dark &&
                        Get(x, y + 5) == ModuleType.Light &&
                        Get(x, y + 6) == ModuleType.Dark &&
                        Get(x, y + 7) == ModuleType.Light &&
                        Get(x, y + 8) == ModuleType.Light &&
                        Get(x, y + 9) == ModuleType.Light &&
                        Get(x, y + 10) == ModuleType.Light)
                        penalty += 40;

                    if (Get(x, y + 0) == ModuleType.Light &&
                        Get(x, y + 1) == ModuleType.Light &&
                        Get(x, y + 2) == ModuleType.Light &&
                        Get(x, y + 3) == ModuleType.Light &&
                        Get(x, y + 4) == ModuleType.Dark &&
                        Get(x, y + 5) == ModuleType.Light &&
                        Get(x, y + 6) == ModuleType.Dark &&
                        Get(x, y + 7) == ModuleType.Dark &&
                        Get(x, y + 8) == ModuleType.Dark &&
                        Get(x, y + 9) == ModuleType.Light &&
                        Get(x, y + 10) == ModuleType.Dark)
                        penalty += 40;
                }
            }

            // ratio penalties
            int total = dim * dim;
            int darkCount = 0;

            for (int x = 0; x < dim; x++)
            {
                for (int y = 0; y < dim; y++)
                {
                    if (Get(x, y) == ModuleType.Dark)
                        darkCount++;
                }
            }

            int percentDark = darkCount * 100 / total;
            int up = (percentDark % 5 == 0) ? percentDark : percentDark + (5 - (percentDark % 5));
            int down = (percentDark % 5 == 0) ? percentDark : percentDark - (percentDark % 5);
            up = Math.Abs(up - 50);
            down = Math.Abs(down - 50);
            up /= 5;
            down /= 5;
            penalty += Math.Min(up, down) * 10;

            return penalty;
        }
        #endregion

        #region Calculation Helpers
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

        private IEnumerable<Tuple<int, int>> GetAlignmentPatternLocations()
        {
            switch (Type)
            {
                case SymbolType.Micro:
                    break;

                case SymbolType.Normal:
                    var locations = AlignmentPatternLocations[Version];
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

        private IEnumerable<Mode> GetAvailableModes(SymbolType type, int version)
        {
            switch (type)
            {
                case SymbolType.Normal:
                    return NormalModes;

                case SymbolType.Micro:
                    return MicroModes[version];

                default:
                    throw new InvalidOperationException();
            }
        }

        private IEnumerable<ErrorCorrection> GetAvailableErrorCorrectionLevels(SymbolType type, int version)
        {
            switch (type)
            {
                case SymbolType.Normal:
                    return NormalErrorCorrectionLevels;

                case SymbolType.Micro:
                    return MicroErrorCorrectionLevels[version];

                default:
                    throw new InvalidOperationException();
            }
        }

        public BitArray EncodeMode(Mode mode)
        {
            switch (Type)
            {
                case SymbolType.Normal:
                    return NormalModeEncodings[(int)mode];

                case SymbolType.Micro:
                    return MicroModeEncodings.First(t => t.Item1 == Version && t.Item2 == mode).Item3;
            }

            throw new InvalidOperationException();
        }

        private BitArray EncodeCharacterCount(Mode mode, int count)
        {
            int bits = GetCharacterCountBits(mode);

            int min = 1;
            int max = GetMaxCharacters(mode);

            if (count < min || count > max)
                throw new ArgumentOutOfRangeException("count", String.Format("QR {0} character counts must be in the range {1} <= n <= {2}", Description, min, max));

            return count.ToBitArray(bits);
        }

        private int GetCharacterCountBits(Mode mode)
        {
            return CharacterWidthTable.First(f => f.Item1 == Type && f.Item2 == Version && f.Item3 == mode).Item4;
        }

        private int GetMaxCharacters(Mode mode)
        {
            return (1 << GetCharacterCountBits(mode)) - 1;
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

        #region Data Tables
        private static readonly Dictionary<char, int> AlphaNumericTable =
            new Dictionary<char, int>()
            {
                { '0',  0 },
                { '1',  1 },
                { '2',  2 },
                { '3',  3 },
                { '4',  4 },
                { '5',  5 },
                { '6',  6 },
                { '7',  7 },
                { '8',  8 },
                { '9',  9 },
                { 'A', 10 },
                { 'B', 11 },
                { 'C', 12 },
                { 'D', 13 },
                { 'E', 14 },
                { 'F', 15 },
                { 'G', 16 },
                { 'H', 17 },
                { 'I', 18 },
                { 'J', 19 },
                { 'K', 20 },
                { 'L', 21 },
                { 'M', 22 },
                { 'N', 23 },
                { 'O', 24 },
                { 'P', 25 },
                { 'Q', 26 },
                { 'R', 27 },
                { 'S', 28 },
                { 'T', 29 },
                { 'U', 30 },
                { 'V', 31 },
                { 'W', 32 },
                { 'X', 33 },
                { 'Y', 34 },
                { 'Z', 35 },
                { ' ', 36 },
                { '$', 37 },
                { '%', 38 },
                { '*', 39 },
                { '+', 40 },
                { '-', 41 },
                { '.', 42 },
                { '/', 43 },
                { ':', 44 }
            };

        private static readonly int[][] AlignmentPatternLocations =
            new int[][]
            {
                new int[] { },
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

        private static readonly Mode[] NormalModes =
            new Mode[]
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

        private static readonly Mode[][] MicroModes =
            new Mode[][]
            {
                null,
                new Mode[] { Mode.Numeric, Mode.Terminator },
                new Mode[] { Mode.Numeric, Mode.AlphaNumeric, Mode.Terminator },
                new Mode[] { Mode.Numeric, Mode.AlphaNumeric, Mode.Byte, Mode.Kanji, Mode.Terminator },
                new Mode[] { Mode.Numeric, Mode.AlphaNumeric, Mode.Byte, Mode.Kanji, Mode.Terminator }
            };

        private static readonly ErrorCorrection[] NormalErrorCorrectionLevels =
            new ErrorCorrection[]
            {
                ErrorCorrection.L,
                ErrorCorrection.M,
                ErrorCorrection.Q,
                ErrorCorrection.H,
            };

        private static readonly ErrorCorrection[][] MicroErrorCorrectionLevels =
            new ErrorCorrection[][]
            {
                null,
                new ErrorCorrection[] { ErrorCorrection.None },
                new ErrorCorrection[] { ErrorCorrection.L, ErrorCorrection.M },
                new ErrorCorrection[] { ErrorCorrection.L, ErrorCorrection.M },
                new ErrorCorrection[] { ErrorCorrection.L, ErrorCorrection.M, ErrorCorrection.Q }
            };

        private static readonly BitArray[] NormalModeEncodings =
            new BitArray[]
            {
                new BitArray(new bool[] { false,  true,  true,  true }),
                new BitArray(new bool[] { false, false, false,  true }),
                new BitArray(new bool[] { false, false,  true, false }),
                new BitArray(new bool[] { false,  true, false, false }),
                new BitArray(new bool[] {  true, false, false, false }),
                null,                
                new BitArray(new bool[] { false,  true, false,  true }),
                new BitArray(new bool[] {  true, false, false,  true }),
                new BitArray(new bool[] { false, false, false, false }),
            };

        private static readonly byte[] ErrorCorrectionEncodings = new byte[] { 0, 1, 0, 3, 2 };

        private static readonly List<Tuple<int, Mode, BitArray>> MicroModeEncodings =
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
            };

        private static readonly Tuple<SymbolType, int, Mode, int>[] CharacterWidthTable =
            new Tuple<SymbolType, int, Mode, int>[]
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
            };

        private static readonly Tuple<SymbolType, int, ErrorCorrection, Tuple<int, int, int, int>[]>[] ErrorCorrectionTable =
            new Tuple<SymbolType, int, ErrorCorrection, Tuple<int, int, int, int>[]>[]
            {
                Tuple.Create(SymbolType.Micro,   1, ErrorCorrection.None, new[] { Tuple.Create( 1,   5,   3,   0) }),
                Tuple.Create(SymbolType.Micro,   2, ErrorCorrection.L,    new[] { Tuple.Create( 1,  10,   5,   1) }),
                Tuple.Create(SymbolType.Micro,   2, ErrorCorrection.M,    new[] { Tuple.Create( 1,  10,   4,   2) }),
                Tuple.Create(SymbolType.Micro,   3, ErrorCorrection.L,    new[] { Tuple.Create( 1,  17,  11,   2) }),
                Tuple.Create(SymbolType.Micro,   3, ErrorCorrection.M,    new[] { Tuple.Create( 1,  17,   9,   4) }),
                Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.L,    new[] { Tuple.Create( 1,  24,  16,   3) }),
                Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.M,    new[] { Tuple.Create( 1,  24,  14,   5) }),
                Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.Q,    new[] { Tuple.Create( 1,  24,  10,   7) }),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.L,    new[] { Tuple.Create( 1,  26,  19,   2) }),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.M,    new[] { Tuple.Create( 1,  26,  16,   4) }),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.Q,    new[] { Tuple.Create( 1,  26,  13,   6) }),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.H,    new[] { Tuple.Create( 1,  26,   9,   8) }),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.L,    new[] { Tuple.Create( 1,  44,  34,   4) }),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.M,    new[] { Tuple.Create( 1,  44,  28,   8) }),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.Q,    new[] { Tuple.Create( 1,  44,  22,  11) }),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.H,    new[] { Tuple.Create( 1,  44,  16,  14) }),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.L,    new[] { Tuple.Create( 1,  70,  55,   7) }),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.M,    new[] { Tuple.Create( 1,  70,  44,  13) }),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.Q,    new[] { Tuple.Create( 2,  35,  17,   9) }),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.H,    new[] { Tuple.Create( 2,  35,  13,  11) }),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.L,    new[] { Tuple.Create( 1, 100,  80,  10) }),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.M,    new[] { Tuple.Create( 2,  50,  32,   9) }),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.Q,    new[] { Tuple.Create( 2,  50,  24,  13) }),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.H,    new[] { Tuple.Create( 4,  25,   9,   8) }),
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.L,    new[] { Tuple.Create( 1, 134, 108,  13) }),
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.M,    new[] { Tuple.Create( 2,  67,  43,  12) }),
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.Q,    new[] { Tuple.Create( 2,  33,  15,   9), 
                                                                                  Tuple.Create( 2,  34,  16,   9) }),
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.H,    new[] { Tuple.Create( 2,  33,  11,  11), 
                                                                                  Tuple.Create( 2,  34,  12,  11) }),
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.L,    new[] { Tuple.Create( 2,  86,  68,   9) }),
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.M,    new[] { Tuple.Create( 4,  43,  27,   8) }),
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.Q,    new[] { Tuple.Create( 4,  43,  19,  12) }),
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.H,    new[] { Tuple.Create( 4,  43,  15,  14) }),
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.L,    new[] { Tuple.Create( 2,  98,  78,  10) }),
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.M,    new[] { Tuple.Create( 4,  49,  31,   9) }),
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.Q,    new[] { Tuple.Create( 2,  32,  14,   9), 
                                                                                  Tuple.Create( 4,  33,  15,   9) }),
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.H,    new[] { Tuple.Create( 4,  39,  13,  13), 
                                                                                  Tuple.Create( 1,  40,  14,  13) }),
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.L,    new[] { Tuple.Create( 2, 121,  97,  12) }),
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.M,    new[] { Tuple.Create( 2,  60,  38,  11),
                                                                                  Tuple.Create( 2,  61,  39,  11) }),
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.Q,    new[] { Tuple.Create( 4,  40,  18,  11), 
                                                                                  Tuple.Create( 2,  41,  19,  11) }),
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.H,    new[] { Tuple.Create( 4,  40,  14,  13), 
                                                                                  Tuple.Create( 2,  41,  15,  13) }),
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.L,    new[] { Tuple.Create( 2, 146, 116,  15) }),
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.M,    new[] { Tuple.Create( 3,  58,  36,  11),
                                                                                  Tuple.Create( 2,  59,  37,  11) }),
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.Q,    new[] { Tuple.Create( 4,  36,  16,  10), 
                                                                                  Tuple.Create( 4,  37,  17,  10) }),
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.H,    new[] { Tuple.Create( 4,  36,  12,  12), 
                                                                                  Tuple.Create( 4,  37,  13,  12) }),
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.L,    new[] { Tuple.Create( 2,  86,  68,   9),
                                                                                  Tuple.Create( 2,  87,  69,   9) }),
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.M,    new[] { Tuple.Create( 4,  69,  43,  13),
                                                                                  Tuple.Create( 1,  70,  44,  13) }),
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.Q,    new[] { Tuple.Create( 6,  43,  19,  12), 
                                                                                  Tuple.Create( 2,  44,  20,  12) }),
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.H,    new[] { Tuple.Create( 6,  43,  15,  14), 
                                                                                  Tuple.Create( 2,  44,  16,  14) }),
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.L,    new[] { Tuple.Create( 4, 101,  81,  10) }),
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.M,    new[] { Tuple.Create( 1,  80,  50,  15),
                                                                                  Tuple.Create( 4,  81,  51,  15) }),
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.Q,    new[] { Tuple.Create( 4,  50,  22,  14), 
                                                                                  Tuple.Create( 4,  51,  23,  14) }),
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.H,    new[] { Tuple.Create( 3,  36,  12,  12), 
                                                                                  Tuple.Create( 8,  37,  13,  12) }),
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.L,    new[] { Tuple.Create( 2, 116,  92,  12),
                                                                                  Tuple.Create( 2, 117,  93,  12) }),
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.M,    new[] { Tuple.Create( 6,  58,  36,  11),
                                                                                  Tuple.Create( 2,  59,  37,  11) }),
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.Q,    new[] { Tuple.Create( 4,  46,  20,  13), 
                                                                                  Tuple.Create( 6,  47,  21,  13) }),
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.H,    new[] { Tuple.Create( 7,  42,  14,  14), 
                                                                                  Tuple.Create( 4,  43,  15,  14) }),
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.L,    new[] { Tuple.Create( 4, 133, 107,  13) }),
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.M,    new[] { Tuple.Create( 8,  59,  37,  11),
                                                                                  Tuple.Create( 1,  60,  38,  11) }),
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.Q,    new[] { Tuple.Create( 8,  44,  20,  12), 
                                                                                  Tuple.Create( 4,  45,  21,  12) }),
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.H,    new[] { Tuple.Create(12,  33,  11,  11), 
                                                                                  Tuple.Create( 4,  34,  12,  11) }),
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.L,    new[] { Tuple.Create( 3, 145, 115,  15),
                                                                                  Tuple.Create( 1, 146, 116,  15) }),
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.M,    new[] { Tuple.Create( 4,  64,  40,  12),
                                                                                  Tuple.Create( 5,  65,  41,  12) }),
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.Q,    new[] { Tuple.Create(11,  36,  16,  10), 
                                                                                  Tuple.Create( 5,  37,  17,  10) }),
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.H,    new[] { Tuple.Create(11,  36,  12,  12), 
                                                                                  Tuple.Create( 5,  37,  13,  12) }),
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.L,    new[] { Tuple.Create( 5, 109,  87,  11),
                                                                                  Tuple.Create( 1, 110,  88,  11) }),
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.M,    new[] { Tuple.Create( 5,  65,  41,  12),
                                                                                  Tuple.Create( 5,  66,  42,  12) }),
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.Q,    new[] { Tuple.Create( 5,  54,  24,  15), 
                                                                                  Tuple.Create( 7,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.H,    new[] { Tuple.Create(11,  36,  12,  12), 
                                                                                  Tuple.Create( 7,  37,  13,  12) }),
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.L,    new[] { Tuple.Create( 5, 122,  98,  12),
                                                                                  Tuple.Create( 1, 123,  99,  12) }),
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.M,    new[] { Tuple.Create( 7,  73,  45,  14),
                                                                                  Tuple.Create( 3,  74,  46,  14) }),
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.Q,    new[] { Tuple.Create(15,  43,  19,  12), 
                                                                                  Tuple.Create( 2,  44,  20,  12) }),
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.H,    new[] { Tuple.Create( 3,  45,  15,  15), 
                                                                                  Tuple.Create(13,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.L,    new[] { Tuple.Create( 1, 135, 107,  14),
                                                                                  Tuple.Create( 5, 136, 108,  14) }),
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.M,    new[] { Tuple.Create(10,  74,  46,  14),
                                                                                  Tuple.Create( 1,  75,  47,  14) }),
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.Q,    new[] { Tuple.Create( 1,  50,  22,  14), 
                                                                                  Tuple.Create(15,  51,  23,  14) }),
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.H,    new[] { Tuple.Create( 2,  42,  14,  14), 
                                                                                  Tuple.Create(17,  43,  15,  14) }),
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.L,    new[] { Tuple.Create( 5, 150, 120,  15),
                                                                                  Tuple.Create( 1, 151, 121,  15) }),
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.M,    new[] { Tuple.Create( 9,  69,  43,  13),
                                                                                  Tuple.Create( 4,  70,  44,  13) }),
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.Q,    new[] { Tuple.Create(17,  50,  22,  14), 
                                                                                  Tuple.Create( 1,  51,  23,  14) }),
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.H,    new[] { Tuple.Create( 2,  42,  14,  14), 
                                                                                  Tuple.Create(19,  43,  15,  14) }),
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.L,    new[] { Tuple.Create( 3, 141, 113,  14),
                                                                                  Tuple.Create( 4, 142, 114,  14) }),
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.M,    new[] { Tuple.Create( 3,  70,  44,  13),
                                                                                  Tuple.Create(11,  71,  45,  13) }),
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.Q,    new[] { Tuple.Create(17,  47,  21,  13), 
                                                                                  Tuple.Create( 4,  48,  22,  13) }),
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.H,    new[] { Tuple.Create( 9,  39,  13,  13), 
                                                                                  Tuple.Create(16,  40,  16,  13) }),
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.L,    new[] { Tuple.Create( 3, 135, 107,  14),
                                                                                  Tuple.Create( 5, 136, 108,  14) }),
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.M,    new[] { Tuple.Create( 3,  67,  41,  13),
                                                                                  Tuple.Create(13,  68,  42,  13) }),
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.Q,    new[] { Tuple.Create(15,  54,  24,  15), 
                                                                                  Tuple.Create( 5,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.H,    new[] { Tuple.Create(15,  43,  15,  14), 
                                                                                  Tuple.Create(10,  44,  16,  14) }),
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.L,    new[] { Tuple.Create( 4, 144, 116,  14),
                                                                                  Tuple.Create( 4, 145, 117,  14) }),
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.M,    new[] { Tuple.Create(17,  68,  42,  13) }),
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.Q,    new[] { Tuple.Create(17,  50,  22,  14), 
                                                                                  Tuple.Create( 6,  51,  23,  14) }),
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.H,    new[] { Tuple.Create(19,  46,  16,  15), 
                                                                                  Tuple.Create( 6,  47,  17,  15) }),
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.L,    new[] { Tuple.Create( 2, 139, 111,  14),
                                                                                  Tuple.Create( 7, 140, 112,  14) }),
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.M,    new[] { Tuple.Create(17,  74,  46,  14) }),
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.Q,    new[] { Tuple.Create( 7,  54,  24,  15), 
                                                                                  Tuple.Create(16,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.H,    new[] { Tuple.Create(34,  37,  13,  12) }),
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.L,    new[] { Tuple.Create( 4, 151, 121,  15),
                                                                                  Tuple.Create( 5, 152, 122,  15) }),
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.M,    new[] { Tuple.Create( 4,  75,  47,  14),
                                                                                  Tuple.Create(14,  76,  48,  14) }),
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.Q,    new[] { Tuple.Create(11,  54,  24,  15), 
                                                                                  Tuple.Create(14,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.H,    new[] { Tuple.Create(16,  45,  15,  15), 
                                                                                  Tuple.Create(14,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.L,    new[] { Tuple.Create( 6, 147, 117,  15),
                                                                                  Tuple.Create( 4, 148, 118,  15) }),
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.M,    new[] { Tuple.Create( 6,  73,  45,  14),
                                                                                  Tuple.Create(14,  74,  46,  14) }),
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.Q,    new[] { Tuple.Create(11,  54,  24,  15), 
                                                                                  Tuple.Create(16,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.H,    new[] { Tuple.Create(30,  46,  16,  15), 
                                                                                  Tuple.Create( 2,  47,  17,  15) }),
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.L,    new[] { Tuple.Create( 8, 132, 106,  13),
                                                                                  Tuple.Create( 4, 133, 107,  13) }),
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.M,    new[] { Tuple.Create( 8,  75,  46,  14),
                                                                                  Tuple.Create(13,  76,  48,  14) }),
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.Q,    new[] { Tuple.Create( 7,  54,  24,  15), 
                                                                                  Tuple.Create(22,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.H,    new[] { Tuple.Create(22,  45,  15,  15), 
                                                                                  Tuple.Create(13,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.L,    new[] { Tuple.Create(10, 142, 114,  14),
                                                                                  Tuple.Create( 2, 143, 115,  14) }),
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.M,    new[] { Tuple.Create(19,  74,  46,  14),
                                                                                  Tuple.Create( 4,  75,  47,  14) }),
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.Q,    new[] { Tuple.Create(28,  50,  22,  14), 
                                                                                  Tuple.Create( 6,  51,  23,  14) }),
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.H,    new[] { Tuple.Create(33,  46,  16,  15), 
                                                                                  Tuple.Create( 4,  47,  17,  15) }),
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.L,    new[] { Tuple.Create( 8, 152, 122,  15),
                                                                                  Tuple.Create( 4, 153, 123,  15) }),
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.M,    new[] { Tuple.Create(22,  73,  45,  14),
                                                                                  Tuple.Create( 3,  74,  46,  14) }),
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.Q,    new[] { Tuple.Create( 8,  53,  23,  15), 
                                                                                  Tuple.Create(26,  54,  24,  15) }),
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.H,    new[] { Tuple.Create(12,  45,  15,  15), 
                                                                                  Tuple.Create(28,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.L,    new[] { Tuple.Create( 3, 147, 117,  15),
                                                                                  Tuple.Create(10, 148, 118,  15) }),
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.M,    new[] { Tuple.Create( 3,  73,  45,  14),
                                                                                  Tuple.Create(23,  74,  46,  14) }),
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.Q,    new[] { Tuple.Create( 4,  54,  24,  15), 
                                                                                  Tuple.Create(31,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.H,    new[] { Tuple.Create(11,  45,  15,  15), 
                                                                                  Tuple.Create(31,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.L,    new[] { Tuple.Create( 7, 146, 116,  15),
                                                                                  Tuple.Create( 7, 147, 117,  15) }),
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.M,    new[] { Tuple.Create(21,  73,  45,  14),
                                                                                  Tuple.Create( 7,  74,  46,  14) }),
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.Q,    new[] { Tuple.Create( 1,  53,  23,  15), 
                                                                                  Tuple.Create(37,  54,  24,  15) }),
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.H,    new[] { Tuple.Create(19,  45,  15,  15), 
                                                                                  Tuple.Create(26,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.L,    new[] { Tuple.Create( 5, 145, 115,  15),
                                                                                  Tuple.Create(10, 146, 116,  15) }),
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.M,    new[] { Tuple.Create(19,  75,  47,  14),
                                                                                  Tuple.Create(10,  76,  48,  14) }),
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.Q,    new[] { Tuple.Create(15,  54,  24,  15), 
                                                                                  Tuple.Create(25,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.H,    new[] { Tuple.Create(23,  45,  15,  15), 
                                                                                  Tuple.Create(25,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.L,    new[] { Tuple.Create(13, 145, 115,  15),
                                                                                  Tuple.Create( 3, 146, 116,  15) }),
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.M,    new[] { Tuple.Create( 2,  74,  46,  14),
                                                                                  Tuple.Create(29,  75,  47,  14) }),
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.Q,    new[] { Tuple.Create(42,  54,  24,  15), 
                                                                                  Tuple.Create( 1,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.H,    new[] { Tuple.Create(23,  45,  15,  15), 
                                                                                  Tuple.Create(28,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.L,    new[] { Tuple.Create(17, 145, 115,  15) }),
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.M,    new[] { Tuple.Create(10,  74,  46,  14),
                                                                                  Tuple.Create(23,  75,  47,  14) }),
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.Q,    new[] { Tuple.Create(10,  54,  24,  15), 
                                                                                  Tuple.Create(35,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.H,    new[] { Tuple.Create(19,  45,  15,  15), 
                                                                                  Tuple.Create(35,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.L,    new[] { Tuple.Create(17, 145, 115,  15),
                                                                                  Tuple.Create( 1, 146, 116,  15) }),
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.M,    new[] { Tuple.Create(14,  74,  46,  14),
                                                                                  Tuple.Create(21,  75,  47,  14) }),
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.Q,    new[] { Tuple.Create(29,  54,  24,  15), 
                                                                                  Tuple.Create(19,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.H,    new[] { Tuple.Create(11,  45,  15,  15), 
                                                                                  Tuple.Create(46,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.L,    new[] { Tuple.Create(13, 145, 115,  15),
                                                                                  Tuple.Create( 6, 146, 116,  15) }),
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.M,    new[] { Tuple.Create(14,  74,  46,  14),
                                                                                  Tuple.Create(23,  75,  47,  14) }),
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.Q,    new[] { Tuple.Create(44,  54,  24,  15), 
                                                                                  Tuple.Create( 7,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.H,    new[] { Tuple.Create(59,  46,  16,  15), 
                                                                                  Tuple.Create( 1,  47,  17,  15) }),
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.L,    new[] { Tuple.Create(12, 151, 121,  15),
                                                                                  Tuple.Create( 7, 152, 122,  15) }),
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.M,    new[] { Tuple.Create(12,  75,  47,  14),
                                                                                  Tuple.Create(26,  76,  48,  14) }),
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.Q,    new[] { Tuple.Create(39,  54,  24,  15), 
                                                                                  Tuple.Create(14,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.H,    new[] { Tuple.Create(22,  45,  15,  15), 
                                                                                  Tuple.Create(41,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.L,    new[] { Tuple.Create( 6, 151, 121,  15),
                                                                                  Tuple.Create(14, 152, 122,  15) }),
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.M,    new[] { Tuple.Create( 6,  75,  47,  14),
                                                                                  Tuple.Create(34,  76,  48,  14) }),
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.Q,    new[] { Tuple.Create(46,  54,  24,  15), 
                                                                                  Tuple.Create(10,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.H,    new[] { Tuple.Create( 2,  45,  15,  15), 
                                                                                  Tuple.Create(64,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.L,    new[] { Tuple.Create(17, 152, 122,  15),
                                                                                  Tuple.Create( 4, 153, 123,  15) }),
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.M,    new[] { Tuple.Create(29,  74,  46,  14),
                                                                                  Tuple.Create(14,  75,  47,  14) }),
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.Q,    new[] { Tuple.Create(49,  54,  24,  15), 
                                                                                  Tuple.Create(10,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.H,    new[] { Tuple.Create(24,  45,  15,  15), 
                                                                                  Tuple.Create(46,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.L,    new[] { Tuple.Create( 4, 152, 122,  15),
                                                                                  Tuple.Create(18, 153, 123,  15) }),
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.M,    new[] { Tuple.Create(13,  74,  46,  14),
                                                                                  Tuple.Create(32,  75,  47,  14) }),
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.Q,    new[] { Tuple.Create(48,  54,  24,  15), 
                                                                                  Tuple.Create(14,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.H,    new[] { Tuple.Create(42,  45,  15,  15), 
                                                                                  Tuple.Create(32,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.L,    new[] { Tuple.Create(20, 147, 117,  15),
                                                                                  Tuple.Create( 4, 148, 118,  15) }),
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.M,    new[] { Tuple.Create(40,  75,  47,  14),
                                                                                  Tuple.Create( 7,  76,  48,  14) }),
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.Q,    new[] { Tuple.Create(43,  54,  24,  15), 
                                                                                  Tuple.Create(22,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.H,    new[] { Tuple.Create(10,  45,  15,  15), 
                                                                                  Tuple.Create(67,  46,  16,  15) }),
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.L,    new[] { Tuple.Create(19, 148, 118,  15),
                                                                                  Tuple.Create( 6, 149, 119,  15) }),
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.M,    new[] { Tuple.Create(18,  75,  47,  14),
                                                                                  Tuple.Create(31,  76,  48,  14) }),
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.Q,    new[] { Tuple.Create(34,  54,  24,  15), 
                                                                                  Tuple.Create(34,  55,  25,  15) }),
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.H,    new[] { Tuple.Create(20,  45,  15,  15), 
                                                                                  Tuple.Create(61,  46,  16,  15) }),
            };

        private static readonly Tuple<SymbolType, int, ErrorCorrection, Tuple<int, int, int, int, int>>[] DataCapacityTable =
            new Tuple<SymbolType, int, ErrorCorrection, Tuple<int, int, int, int, int>>[]
            {
                Tuple.Create(SymbolType.Micro,   1, ErrorCorrection.None, Tuple.Create(   3,    5,    0,    0,    0)),
                Tuple.Create(SymbolType.Micro,   2, ErrorCorrection.L,    Tuple.Create(   5,   10,    6,    0,    0)),
                Tuple.Create(SymbolType.Micro,   2, ErrorCorrection.M,    Tuple.Create(   4,    8,    5,    0,    0)),
                Tuple.Create(SymbolType.Micro,   3, ErrorCorrection.L,    Tuple.Create(  11,   23,   14,    9,    6)),
                Tuple.Create(SymbolType.Micro,   3, ErrorCorrection.M,    Tuple.Create(   9,   18,   11,    7,    4)),
                Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.L,    Tuple.Create(  16,   35,   21,   15,    9)),
                Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.M,    Tuple.Create(  14,   30,   18,   13,    8)),
                Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.Q,    Tuple.Create(  10,   21,   13,    9,    5)),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.L,    Tuple.Create(  19,   41,   25,   15,    9)),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.M,    Tuple.Create(  16,   34,   20,   14,    8)),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.Q,    Tuple.Create(  13,   27,   16,   11,    7)),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.H,    Tuple.Create(   9,   17,   10,    7,    4)),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.L,    Tuple.Create(  34,   77,   47,   32,   20)),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.M,    Tuple.Create(  28,   63,   38,   26,   16)),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.Q,    Tuple.Create(  22,   48,   29,   20,   12)),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.H,    Tuple.Create(  16,   34,   20,   14,    8)),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.L,    Tuple.Create(  55,  127,   77,   53,   32)),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.M,    Tuple.Create(  44,  101,   61,   42,   26)),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.Q,    Tuple.Create(  34,   77,   47,   32,   20)),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.H,    Tuple.Create(  26,   58,   35,   24,   15)),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.L,    Tuple.Create(  80,  187,  114,   78,   48)),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.M,    Tuple.Create(  64,  149,   90,   62,   38)),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.Q,    Tuple.Create(  48,  111,   67,   46,   28)),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.H,    Tuple.Create(  36,   82,   50,   34,   21)),
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.L,    Tuple.Create( 108,  255,  154,  106,   65)),
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.M,    Tuple.Create(  86,  202,  122,   84,   52)),
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.Q,    Tuple.Create(  62,  144,   87,   60,   37)), 
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.H,    Tuple.Create(  46,  106,   64,   44,   27)), 
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.L,    Tuple.Create( 136,  322,  195,  134,   82)),
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.M,    Tuple.Create( 108,  255,  154,  106,   65)),
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.Q,    Tuple.Create(  76,  178,  108,   74,   45)),
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.H,    Tuple.Create(  60,  139,   84,   58,   36)),
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.L,    Tuple.Create( 156,  370,  224,  154,   95)),
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.M,    Tuple.Create( 124,  293,  178,  122,   75)),
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.Q,    Tuple.Create(  88,  207,  125,   86,   53)), 
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.H,    Tuple.Create(  66,  154,   93,   64,   39)), 
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.L,    Tuple.Create( 194,  461,  279,  192,  118)),
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.M,    Tuple.Create( 154,  365,  221,  152,   93)),
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.Q,    Tuple.Create( 110,  259,  157,  108,   66)), 
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.H,    Tuple.Create(  86,  202,  122,   84,   52)), 
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.L,    Tuple.Create( 232,  552,  335,  230,  141)),
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.M,    Tuple.Create( 182,  432,  262,  180,  111)),
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.Q,    Tuple.Create( 132,  312,  189,  130,   80)), 
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.H,    Tuple.Create( 100,  235,  143,   98,   60)), 
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.L,    Tuple.Create( 274,  652,  395,  271,  167)),
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.M,    Tuple.Create( 216,  513,  311,  213,  131)),
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.Q,    Tuple.Create( 154,  364,  221,  151,   93)), 
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.H,    Tuple.Create( 122,  288,  174,  119,   74)), 
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.L,    Tuple.Create( 324,  772,  468,  321,  198)),
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.M,    Tuple.Create( 254,  604,  366,  251,  155)),
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.Q,    Tuple.Create( 180,  427,  259,  177,  109)), 
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.H,    Tuple.Create( 140,  331,  200,  137,   85)), 
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.L,    Tuple.Create( 370,  883,  535,  367,  226)),
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.M,    Tuple.Create( 290,  691,  419,  287,  177)),
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.Q,    Tuple.Create( 206,  489,  296,  203,  125)), 
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.H,    Tuple.Create( 158,  374,  227,  155,   96)), 
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.L,    Tuple.Create( 428, 1022,  619,  425,  262)),
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.M,    Tuple.Create( 334,  796,  483,  331,  204)),
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.Q,    Tuple.Create( 244,  580,  352,  241,  149)), 
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.H,    Tuple.Create( 180,  427,  259,  177,  109)), 
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.L,    Tuple.Create( 461, 1101,  667,  458,  282)),
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.M,    Tuple.Create( 365,  871,  528,  362,  223)),
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.Q,    Tuple.Create( 291,  621,  376,  258,  159)), 
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.H,    Tuple.Create( 197,  468,  283,  194,  120)), 
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.L,    Tuple.Create( 523, 1250,  758,  520,  320)),
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.M,    Tuple.Create( 415,  991,  600,  412,  254)),
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.Q,    Tuple.Create( 295,  703,  426,  292,  180)), 
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.H,    Tuple.Create( 223,  530,  321,  220,  136)), 
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.L,    Tuple.Create( 589, 1408,  854,  586,  361)),
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.M,    Tuple.Create( 453, 1082,  656,  450,  277)),
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.Q,    Tuple.Create( 325,  775,  470,  322,  198)), 
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.H,    Tuple.Create( 253,  602,  365,  250,  154)), 
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.L,    Tuple.Create( 647, 1548,  938,  644,  397)),
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.M,    Tuple.Create( 507, 1212,  734,  504,  310)),
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.Q,    Tuple.Create( 367,  876,  531,  364,  224)), 
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.H,    Tuple.Create( 283,  674,  408,  280,  173)), 
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.L,    Tuple.Create( 721, 1725, 1046,  718,  442)),
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.M,    Tuple.Create( 563, 1346,  816,  560,  345)),
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.Q,    Tuple.Create( 397,  948,  574,  394,  243)), 
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.H,    Tuple.Create( 313,  746,  452,  310,  191)), 
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.L,    Tuple.Create( 795, 1903, 1153,  792,  488)),
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.M,    Tuple.Create( 627, 1500,  909,  624,  384)),
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.Q,    Tuple.Create( 445, 1063,  644,  442,  272)), 
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.H,    Tuple.Create( 341,  813,  493,  338,  208)), 
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.L,    Tuple.Create( 861, 2061, 1249,  858,  528)),
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.M,    Tuple.Create( 669, 1600,  970,  666,  410)),
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.Q,    Tuple.Create( 445, 1159,  702,  482,  297)), 
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.H,    Tuple.Create( 341,  919,  557,  382,  235)), 
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.L,    Tuple.Create( 932, 2232, 1352,  929,  572)),
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.M,    Tuple.Create( 714, 1708, 1035,  711,  438)),
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.Q,    Tuple.Create( 512, 1224,  742,  509,  314)), 
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.H,    Tuple.Create( 406,  969,  587,  403,  248)), 
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.L,    Tuple.Create(1006, 2409, 1460, 1003,  618)),
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.M,    Tuple.Create( 782, 1872, 1134,  779,  480)),
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.Q,    Tuple.Create( 568, 1358,  823,  565,  348)), 
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.H,    Tuple.Create( 442, 1056,  640,  439,  270)),
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.L,    Tuple.Create(1094, 2620, 1588, 1091,  672)),
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.M,    Tuple.Create( 860, 2059, 1248,  857,  528)),
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.Q,    Tuple.Create( 614, 1468,  890,  611,  376)), 
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.H,    Tuple.Create( 464, 1108,  672,  461,  284)), 
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.L,    Tuple.Create(1174, 2812, 1704, 1171,  721)),
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.M,    Tuple.Create( 914, 2188, 1326,  911,  561)),
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.Q,    Tuple.Create( 664, 1588,  963,  661,  407)), 
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.H,    Tuple.Create( 514, 1228,  744,  511,  315)), 
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.L,    Tuple.Create(1276, 3057, 1853, 1273,  784)),
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.M,    Tuple.Create(1000, 2395, 1451,  997,  614)),
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.Q,    Tuple.Create( 718, 1718, 1041,  715,  440)), 
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.H,    Tuple.Create( 538, 1286,  779,  535,  330)), 
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.L,    Tuple.Create(1370, 3283, 1990, 1367,  842)),
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.M,    Tuple.Create(1062, 2544, 1542, 1059,  652)),
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.Q,    Tuple.Create( 754, 1804, 1094,  751,  462)), 
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.H,    Tuple.Create( 596, 1425,  864,  593,  365)), 
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.L,    Tuple.Create(1468, 3517, 2132, 1465,  902)),
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.M,    Tuple.Create(1128, 2701, 1637, 1125,  692)),
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.Q,    Tuple.Create( 808, 1933, 1172,  805,  496)), 
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.H,    Tuple.Create( 628, 1501,  910,  625,  385)), 
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.L,    Tuple.Create(1531, 3669, 2223, 1528,  940)),
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.M,    Tuple.Create(1193, 2857, 1732, 1190,  732)),
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.Q,    Tuple.Create( 871, 2085, 1263,  868,  534)), 
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.H,    Tuple.Create( 661, 1581,  958,  658,  405)), 
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.L,    Tuple.Create(1631, 3909, 2369, 1628, 1002)),
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.M,    Tuple.Create(1267, 3035, 1839, 1264,  778)),
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.Q,    Tuple.Create( 911, 2181, 1322,  908,  559)), 
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.H,    Tuple.Create( 701, 1677, 1016,  698,  430)), 
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.L,    Tuple.Create(1735, 4158, 2520, 1732, 1066)),
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.M,    Tuple.Create(1373, 3289, 1994, 1370,  843)),
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.Q,    Tuple.Create( 985, 2358, 1429,  982,  604)), 
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.H,    Tuple.Create( 745, 1782, 1080,  742,  457)), 
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.L,    Tuple.Create(1843, 4417, 2677, 1840, 1132)),
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.M,    Tuple.Create(1455, 3486, 2113, 1452,  894)),
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.Q,    Tuple.Create(1033, 2473, 1499, 1030,  634)), 
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.H,    Tuple.Create( 793, 1897, 1150,  790,  486)), 
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.L,    Tuple.Create(1955, 4686, 2840, 1952, 1201)),
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.M,    Tuple.Create(1541, 3693, 2238, 1538,  947)),
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.Q,    Tuple.Create(1115, 2670, 1618, 1112,  684)), 
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.H,    Tuple.Create( 845, 2022, 1226,  842,  518)), 
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.L,    Tuple.Create(2071, 4965, 3009, 2068, 1273)),
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.M,    Tuple.Create(1631, 3909, 2369, 1628, 1002)),
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.Q,    Tuple.Create(1171, 2805, 1700, 1168,  719)), 
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.H,    Tuple.Create( 901, 2157, 1307,  898,  553)), 
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.L,    Tuple.Create(2191, 5253, 3183, 2188, 1347)),
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.M,    Tuple.Create(1725, 4134, 2506, 1722, 1060)),
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.Q,    Tuple.Create(1231, 2949, 1787, 1228,  756)), 
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.H,    Tuple.Create( 961, 2301, 1394,  958,  590)), 
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.L,    Tuple.Create(2306, 5529, 3351, 2303, 1417)),
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.M,    Tuple.Create(1812, 4343, 2632, 1809, 1113)),
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.Q,    Tuple.Create(1286, 3081, 1867, 1283,  790)), 
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.H,    Tuple.Create( 986, 2361, 1431,  983,  605)), 
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.L,    Tuple.Create(2434, 5836, 3537, 2431, 1496)),
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.M,    Tuple.Create(1914, 4588, 2780, 1911, 1176)),
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.Q,    Tuple.Create(1354, 3244, 1966, 1351,  832)), 
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.H,    Tuple.Create(1054, 2524, 1530, 1051,  647)), 
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.L,    Tuple.Create(2566, 6153, 3729, 2563, 1577)),
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.M,    Tuple.Create(1992, 4775, 2894, 1989, 1224)),
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.Q,    Tuple.Create(1426, 3417, 2071, 1423,  876)), 
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.H,    Tuple.Create(1096, 2625, 1591, 1093,  673)), 
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.L,    Tuple.Create(2702, 6479, 3927, 2699, 1661)),
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.M,    Tuple.Create(2102, 5039, 3054, 2099, 1292)),
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.Q,    Tuple.Create(1502, 3599, 2181, 1499,  923)), 
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.H,    Tuple.Create(1142, 2735, 1658, 1139,  701)), 
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.L,    Tuple.Create(2812, 6743, 4087, 2809, 1729)),
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.M,    Tuple.Create(2216, 5313, 3220, 2213, 1362)),
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.Q,    Tuple.Create(1582, 3791, 2298, 1579, 972)), 
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.H,    Tuple.Create(1222, 2927, 1774, 1219,  750)), 
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.L,    Tuple.Create(2956, 7089, 4296, 2953, 1817)),
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.M,    Tuple.Create(2334, 5596, 3391, 2331, 1435)),
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.Q,    Tuple.Create(1666, 3993, 2420, 1663, 1024)), 
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.H,    Tuple.Create(1276, 3057, 1852, 1273,  784)), 
            };

        private static readonly Tuple<byte, byte, Func<int, int, bool>>[] DataMaskTable =
            new Tuple<byte, byte, Func<int, int, bool>>[]
            {
                Tuple.Create<byte, byte, Func<int, int, bool>>(0, 255, (i, j) => (i + j) % 2 == 0),
                Tuple.Create<byte, byte, Func<int, int, bool>>(1,   0, (i, j) => i % 2 == 0),
                Tuple.Create<byte, byte, Func<int, int, bool>>(2, 255, (i, j) => j % 3 == 0),
                Tuple.Create<byte, byte, Func<int, int, bool>>(3, 255, (i, j) => (i + j) % 3 == 0),
                Tuple.Create<byte, byte, Func<int, int, bool>>(4,   1, (i, j) => ((i / 2) + (j / 3)) % 2 == 0),
                Tuple.Create<byte, byte, Func<int, int, bool>>(5, 255, (i, j) => ((i * j) % 2) + ((i * j) % 3) == 0),
                Tuple.Create<byte, byte, Func<int, int, bool>>(6,   2, (i, j) => (((i * j) % 2) + ((i * j) % 3)) % 2 == 0),
                Tuple.Create<byte, byte, Func<int, int, bool>>(7,   3, (i, j) => (((i + j) % 2) + ((i * j) % 3)) % 2 == 0),
            };

        private static readonly Tuple<ErrorCorrection, byte, BitArray>[] NormalFormatStrings =
            new Tuple<ErrorCorrection, byte, BitArray>[]
            {
                Tuple.Create(ErrorCorrection.L, (byte)0, new BitArray(new bool[] {  true,  true,  true, false,  true,  true,  true,  true,  true, false, false, false,  true, false, false })),
                Tuple.Create(ErrorCorrection.L, (byte)1, new BitArray(new bool[] {  true,  true,  true, false, false,  true, false,  true,  true,  true,  true, false, false,  true,  true })),
                Tuple.Create(ErrorCorrection.L, (byte)2, new BitArray(new bool[] {  true,  true,  true,  true,  true, false,  true,  true, false,  true, false,  true, false,  true, false })),
                Tuple.Create(ErrorCorrection.L, (byte)3, new BitArray(new bool[] {  true,  true,  true,  true, false, false, false,  true, false, false,  true,  true,  true, false,  true })),
                Tuple.Create(ErrorCorrection.L, (byte)4, new BitArray(new bool[] {  true,  true, false, false,  true,  true, false, false, false,  true, false,  true,  true,  true,  true })),
                Tuple.Create(ErrorCorrection.L, (byte)5, new BitArray(new bool[] {  true,  true, false, false, false,  true,  true, false, false, false,  true,  true, false, false, false })),
                Tuple.Create(ErrorCorrection.L, (byte)6, new BitArray(new bool[] {  true,  true, false,  true,  true, false, false, false,  true, false, false, false, false, false,  true })),
                Tuple.Create(ErrorCorrection.L, (byte)7, new BitArray(new bool[] {  true,  true, false,  true, false, false,  true, false,  true,  true,  true, false,  true,  true, false })),
                Tuple.Create(ErrorCorrection.M, (byte)0, new BitArray(new bool[] {  true, false,  true, false,  true, false, false, false, false, false,  true, false, false,  true, false })),
                Tuple.Create(ErrorCorrection.M, (byte)1, new BitArray(new bool[] {  true, false,  true, false, false, false,  true, false, false,  true, false, false,  true, false,  true })),
                Tuple.Create(ErrorCorrection.M, (byte)2, new BitArray(new bool[] {  true, false,  true,  true,  true,  true, false, false,  true,  true,  true,  true,  true, false, false })),
                Tuple.Create(ErrorCorrection.M, (byte)3, new BitArray(new bool[] {  true, false,  true,  true, false,  true,  true, false,  true, false, false,  true, false,  true,  true })),
                Tuple.Create(ErrorCorrection.M, (byte)4, new BitArray(new bool[] {  true, false, false, false,  true, false,  true,  true,  true,  true,  true,  true, false, false,  true })),
                Tuple.Create(ErrorCorrection.M, (byte)5, new BitArray(new bool[] {  true, false, false, false, false, false, false,  true,  true, false, false,  true,  true,  true, false })),
                Tuple.Create(ErrorCorrection.M, (byte)6, new BitArray(new bool[] {  true, false, false,  true,  true,  true,  true,  true, false, false,  true, false,  true,  true,  true })),
                Tuple.Create(ErrorCorrection.M, (byte)7, new BitArray(new bool[] {  true, false, false,  true, false,  true, false,  true, false,  true, false, false, false, false, false })),
                Tuple.Create(ErrorCorrection.Q, (byte)0, new BitArray(new bool[] { false,  true,  true, false,  true, false,  true, false,  true, false,  true,  true,  true,  true,  true })),
                Tuple.Create(ErrorCorrection.Q, (byte)1, new BitArray(new bool[] { false,  true,  true, false, false, false, false, false,  true,  true, false,  true, false, false, false })),
                Tuple.Create(ErrorCorrection.Q, (byte)2, new BitArray(new bool[] { false,  true,  true,  true,  true,  true,  true, false, false,  true,  true, false, false, false,  true })),
                Tuple.Create(ErrorCorrection.Q, (byte)3, new BitArray(new bool[] { false,  true,  true,  true, false,  true, false, false, false, false, false, false,  true,  true, false })),
                Tuple.Create(ErrorCorrection.Q, (byte)4, new BitArray(new bool[] { false,  true, false, false,  true, false, false,  true, false,  true,  true, false,  true, false, false })),
                Tuple.Create(ErrorCorrection.Q, (byte)5, new BitArray(new bool[] { false,  true, false, false, false, false,  true,  true, false, false, false, false, false,  true,  true })),
                Tuple.Create(ErrorCorrection.Q, (byte)6, new BitArray(new bool[] { false,  true, false,  true,  true,  true, false,  true,  true, false,  true,  true, false,  true, false })),
                Tuple.Create(ErrorCorrection.Q, (byte)7, new BitArray(new bool[] { false,  true, false,  true, false,  true,  true,  true,  true,  true, false,  true,  true, false,  true })),
                Tuple.Create(ErrorCorrection.H, (byte)0, new BitArray(new bool[] { false, false,  true, false,  true,  true, false,  true, false, false, false,  true, false, false,  true })),
                Tuple.Create(ErrorCorrection.H, (byte)1, new BitArray(new bool[] { false, false,  true, false, false,  true,  true,  true, false,  true,  true,  true,  true,  true, false })),
                Tuple.Create(ErrorCorrection.H, (byte)2, new BitArray(new bool[] { false, false,  true,  true,  true, false, false,  true,  true,  true, false, false,  true,  true,  true })),
                Tuple.Create(ErrorCorrection.H, (byte)3, new BitArray(new bool[] { false, false,  true,  true, false, false,  true,  true,  true, false,  true, false, false, false, false })),
                Tuple.Create(ErrorCorrection.H, (byte)4, new BitArray(new bool[] { false, false, false, false,  true,  true,  true, false,  true,  true, false, false, false,  true, false })),
                Tuple.Create(ErrorCorrection.H, (byte)5, new BitArray(new bool[] { false, false, false, false, false,  true, false, false,  true, false,  true, false,  true, false,  true })),
                Tuple.Create(ErrorCorrection.H, (byte)6, new BitArray(new bool[] { false, false, false,  true,  true, false,  true, false, false, false, false,  true,  true, false, false })),
                Tuple.Create(ErrorCorrection.H, (byte)7, new BitArray(new bool[] { false, false, false,  true, false, false, false, false, false,  true,  true,  true, false,  true,  true }))
            };

        private static readonly Tuple<int, ErrorCorrection, byte, BitArray>[] MicroFormatStrings =
            new Tuple<int, ErrorCorrection, byte, BitArray>[]
            {
                Tuple.Create(2, ErrorCorrection.M,    (byte)0, new BitArray(new bool[] { true,  true, false, false,  true,  true,  true,  true, false, false,  true, false, false,  true,  true })),
                Tuple.Create(2, ErrorCorrection.M,    (byte)1, new BitArray(new bool[] { true,  true, false, false, false,  true, false,  true, false,  true, false, false,  true, false, false })),
                Tuple.Create(2, ErrorCorrection.M,    (byte)2, new BitArray(new bool[] { true,  true, false,  true,  true, false,  true,  true,  true,  true,  true,  true,  true, false,  true })),
                Tuple.Create(2, ErrorCorrection.M,    (byte)3, new BitArray(new bool[] { true,  true, false,  true, false, false, false,  true,  true, false, false,  true, false,  true, false })),
                Tuple.Create(2, ErrorCorrection.L,    (byte)0, new BitArray(new bool[] { true, false,  true, false,  true, false,  true,  true, false,  true, false,  true,  true,  true, false })),
                Tuple.Create(2, ErrorCorrection.L,    (byte)1, new BitArray(new bool[] { true, false,  true, false, false, false, false,  true, false, false,  true,  true, false, false,  true })),
                Tuple.Create(2, ErrorCorrection.L,    (byte)2, new BitArray(new bool[] { true, false,  true,  true,  true,  true,  true,  true,  true, false, false, false, false, false, false })),
                Tuple.Create(2, ErrorCorrection.L,    (byte)3, new BitArray(new bool[] { true, false,  true,  true, false,  true, false,  true,  true,  true,  true, false,  true,  true,  true })),
                Tuple.Create(3, ErrorCorrection.L,    (byte)0, new BitArray(new bool[] { true,  true,  true, false,  true,  true, false, false,  true,  true,  true,  true, false, false, false })),
                Tuple.Create(3, ErrorCorrection.L,    (byte)1, new BitArray(new bool[] { true,  true,  true, false, false,  true,  true, false,  true, false, false,  true,  true,  true,  true })),
                Tuple.Create(3, ErrorCorrection.L,    (byte)2, new BitArray(new bool[] { true,  true,  true,  true,  true, false, false, false, false, false,  true, false,  true,  true, false })),
                Tuple.Create(3, ErrorCorrection.L,    (byte)3, new BitArray(new bool[] { true,  true,  true,  true, false, false,  true, false, false,  true, false, false, false, false,  true })),
                Tuple.Create(3, ErrorCorrection.M,    (byte)0, new BitArray(new bool[] {false, false, false, false,  true,  true, false,  true,  true, false,  true,  true,  true,  true, false })),
                Tuple.Create(3, ErrorCorrection.M,    (byte)1, new BitArray(new bool[] {false, false, false, false, false,  true,  true,  true,  true,  true, false,  true, false, false,  true })),
                Tuple.Create(3, ErrorCorrection.M,    (byte)2, new BitArray(new bool[] {false, false, false,  true,  true, false, false,  true, false,  true,  true, false, false, false, false })),
                Tuple.Create(3, ErrorCorrection.M,    (byte)3, new BitArray(new bool[] {false, false, false,  true, false, false,  true,  true, false, false, false, false,  true,  true,  true })),
                Tuple.Create(1, ErrorCorrection.None, (byte)0, new BitArray(new bool[] { true, false, false, false,  true, false, false, false,  true, false, false, false,true, false,  true })),
                Tuple.Create(1, ErrorCorrection.None, (byte)1, new BitArray(new bool[] { true, false, false, false, false, false,  true, false,  true,  true,  true, false, false,  true, false })),
                Tuple.Create(1, ErrorCorrection.None, (byte)2, new BitArray(new bool[] { true, false, false,  true,  true,  true, false, false, false,  true, false,  true, false,  true,  true })),
                Tuple.Create(1, ErrorCorrection.None, (byte)3, new BitArray(new bool[] { true, false, false,  true, false,  true,  true, false, false, false,  true,  true,true, false, false })),
                Tuple.Create(4, ErrorCorrection.M,    (byte)0, new BitArray(new bool[] {false,  true, false, false,  true, false,  true, false, false, false, false,  true, false, false, false })),
                Tuple.Create(4, ErrorCorrection.M,    (byte)1, new BitArray(new bool[] {false,  true, false, false, false, false, false, false, false,  true,  true,  true,  true,  true,  true })),
                Tuple.Create(4, ErrorCorrection.M,    (byte)2, new BitArray(new bool[] {false,  true, false,  true,  true,  true,  true, false,  true,  true, false, false,  true,  true, false })),
                Tuple.Create(4, ErrorCorrection.M,    (byte)3, new BitArray(new bool[] {false,  true, false,  true, false,  true, false, false,  true, false,  true, false, false, false,  true })),
                Tuple.Create(4, ErrorCorrection.Q,    (byte)0, new BitArray(new bool[] {false,  true,  true, false,  true, false, false,  true,  true,  true, false, false, false,  true,  true })),
                Tuple.Create(4, ErrorCorrection.Q,    (byte)1, new BitArray(new bool[] {false,  true,  true, false, false, false,  true,  true,  true, false,  true, false,  true, false, false })),
                Tuple.Create(4, ErrorCorrection.Q,    (byte)2, new BitArray(new bool[] {false,  true,  true,  true,  true,  true, false,  true, false, false, false,  true,  true, false,  true })),
                Tuple.Create(4, ErrorCorrection.Q,    (byte)3, new BitArray(new bool[] {false,  true,  true,  true, false,  true,  true,  true, false,  true,  true,  true, false,  true, false })),
                Tuple.Create(4, ErrorCorrection.L,    (byte)0, new BitArray(new bool[] {false, false,  true, false,  true,  true,  true, false, false,  true,  true, false,  true, false,  true })),
                Tuple.Create(4, ErrorCorrection.L,    (byte)1, new BitArray(new bool[] {false, false,  true, false, false,  true, false, false, false, false, false, false, false,  true, false })),
                Tuple.Create(4, ErrorCorrection.L,    (byte)2, new BitArray(new bool[] {false, false,  true,  true,  true, false,  true, false,  true, false,  true,  true, false,  true,  true })),
                Tuple.Create(4, ErrorCorrection.L,    (byte)3, new BitArray(new bool[] {false, false,  true,  true, false, false, false, false,  true,  true, false,  true,  true, false, false })),
            };

        private static readonly BitArray[] VersionStrings =
            new BitArray[]
            {
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new BitArray(new bool[] { false, false, false,  true,  true,  true,  true,  true, false, false,  true, false, false,  true, false,  true, false, false }),
                new BitArray(new bool[] { false, false,  true, false, false, false, false,  true, false,  true,  true, false,  true,  true,  true,  true, false, false }),
                new BitArray(new bool[] { false, false,  true, false, false,  true,  true, false,  true, false,  true, false, false,  true,  true, false, false,  true }),
                new BitArray(new bool[] { false, false,  true, false,  true, false, false,  true, false, false,  true,  true, false,  true, false, false,  true,  true }),
                new BitArray(new bool[] { false, false,  true, false,  true,  true,  true, false,  true,  true,  true,  true,  true,  true, false,  true,  true, false }),
                new BitArray(new bool[] { false, false,  true,  true, false, false, false,  true,  true,  true, false,  true,  true, false, false, false,  true, false }),
                new BitArray(new bool[] { false, false,  true,  true, false,  true,  true, false, false, false, false,  true, false, false, false,  true,  true,  true }),
                new BitArray(new bool[] { false, false,  true,  true,  true, false, false,  true,  true, false, false, false, false, false,  true,  true, false,  true }),
                new BitArray(new bool[] { false, false,  true,  true,  true,  true,  true, false, false,  true, false, false,  true, false,  true, false, false, false }),
                new BitArray(new bool[] { false,  true, false, false, false, false,  true, false,  true,  true, false,  true,  true,  true,  true, false, false, false }),
                new BitArray(new bool[] { false,  true, false, false, false,  true, false,  true, false, false, false,  true, false,  true,  true,  true, false,  true }),
                new BitArray(new bool[] { false,  true, false, false,  true, false,  true, false,  true, false, false, false, false,  true, false,  true,  true,  true }),
                new BitArray(new bool[] { false,  true, false, false,  true,  true, false,  true, false,  true, false, false,  true,  true, false, false,  true, false }),
                new BitArray(new bool[] { false,  true, false,  true, false, false,  true, false, false,  true,  true, false,  true, false, false,  true,  true, false }),
                new BitArray(new bool[] { false,  true, false,  true, false,  true, false,  true,  true, false,  true, false, false, false, false, false,  true,  true }),
                new BitArray(new bool[] { false,  true, false,  true,  true, false,  true, false, false, false,  true,  true, false, false,  true, false, false,  true }),
                new BitArray(new bool[] { false,  true, false,  true,  true,  true, false,  true,  true,  true,  true,  true,  true, false,  true,  true, false, false }),
                new BitArray(new bool[] { false,  true,  true, false, false, false,  true,  true,  true, false,  true,  true, false, false, false,  true, false, false }),
                new BitArray(new bool[] { false,  true,  true, false, false,  true, false, false, false,  true,  true,  true,  true, false, false, false, false,  true }),
                new BitArray(new bool[] { false,  true,  true, false,  true, false,  true,  true,  true,  true,  true, false,  true, false,  true, false,  true,  true }),
                new BitArray(new bool[] { false,  true,  true, false,  true,  true, false, false, false, false,  true, false, false, false,  true,  true,  true, false }),
                new BitArray(new bool[] { false,  true,  true,  true, false, false,  true,  true, false, false, false, false, false,  true,  true, false,  true, false }),
                new BitArray(new bool[] { false,  true,  true,  true, false,  true, false, false,  true,  true, false, false,  true,  true,  true,  true,  true,  true }),
                new BitArray(new bool[] { false,  true,  true,  true,  true, false,  true,  true, false,  true, false,  true,  true,  true, false,  true, false,  true }),
                new BitArray(new bool[] { false,  true,  true,  true,  true,  true, false, false,  true, false, false,  true, false,  true, false, false, false, false }),
                new BitArray(new bool[] {  true, false, false, false, false, false,  true, false, false,  true,  true,  true, false,  true, false,  true, false,  true }),
                new BitArray(new bool[] {  true, false, false, false, false,  true, false,  true,  true, false,  true,  true,  true,  true, false, false, false, false }),
                new BitArray(new bool[] {  true, false, false, false,  true, false,  true, false, false, false,  true, false,  true,  true,  true, false,  true, false }),
                new BitArray(new bool[] {  true, false, false, false,  true,  true, false,  true,  true,  true,  true, false, false,  true,  true,  true,  true,  true }),
                new BitArray(new bool[] {  true, false, false,  true, false, false,  true, false,  true,  true, false, false, false, false,  true, false,  true,  true }),
                new BitArray(new bool[] {  true, false, false,  true, false,  true, false,  true, false, false, false, false,  true, false,  true,  true,  true, false }),
                new BitArray(new bool[] {  true, false, false,  true,  true, false,  true, false,  true, false, false,  true,  true, false, false,  true, false, false }),
                new BitArray(new bool[] {  true, false, false,  true,  true,  true, false,  true, false,  true, false,  true, false, false, false, false, false,  true }),
                new BitArray(new bool[] {  true, false,  true, false, false, false,  true,  true, false, false, false,  true,  true, false,  true, false, false,  true }),
            };

        private static readonly byte[] ExponentTable =
            new byte[]
            {
                1,   2,   4,   8,   16,  32,  64,  128, 29,  58,  116, 232, 205, 135, 19,  38,  
                76,  152, 45,  90,  180, 117, 234, 201, 143, 3,   6,   12,  24,  48,  96,  192, 
                157, 39,  78,  156, 37,  74,  148, 53,  106, 212, 181, 119, 238, 193, 159, 35,  
                70,  140, 5,   10,  20,  40,  80,  160, 93,  186, 105, 210, 185, 111, 222, 161, 
                95,  190, 97,  194, 153, 47,  94,  188, 101, 202, 137, 15,  30,  60,  120, 240, 
                253, 231, 211, 187, 107, 214, 177, 127, 254, 225, 223, 163, 91,  182, 113, 226, 
                217, 175, 67,  134, 17,  34,  68,  136, 13,  26,  52,  104, 208, 189, 103, 206, 
                129, 31,  62,  124, 248, 237, 199, 147, 59,  118, 236, 197, 151, 51,  102, 204, 
                133, 23,  46,  92,  184, 109, 218, 169, 79,  158, 33,  66,  132, 21,  42,  84,  
                168, 77,  154, 41,  82,  164, 85,  170, 73,  146, 57,  114, 228, 213, 183, 115, 
                230, 209, 191, 99,  198, 145, 63,  126, 252, 229, 215, 179, 123, 246, 241, 255, 
                227, 219, 171, 75,  150, 49,  98,  196, 149, 55,  110, 220, 165, 87,  174, 65,  
                130, 25,  50,  100, 200, 141, 7,   14,  28,  56,  112, 224, 221, 167, 83,  166, 
                81,  162, 89,  178, 121, 242, 249, 239, 195, 155, 43,  86,  172, 69,  138, 9,   
                18,  36,  72,  144, 61,  122, 244, 245, 247, 243, 251, 235, 203, 139, 11,  22,  
                44,  88,  176, 125, 250, 233, 207, 131, 27,  54,  108, 216, 173, 71,  142, 1
            };

        private static readonly byte[] LogTable =
            new byte[]
            {
                0,   0,   1,   25,  2,   50,  26,  198, 3,   223, 51,  238, 27,  104, 199, 75,
                4,   100, 224, 14,  52,  141, 239, 129, 28,  193, 105, 248, 200, 8,   76,  113,
                5,   138, 101, 47,  225, 36,  15,  33,  53,  147, 142, 218, 240, 18,  130, 69,
                29,  181, 194, 125, 106, 39,  249, 185, 201, 154, 9,   120, 77,  228, 114, 166,
                6,   191, 139, 98,  102, 221, 48,  253, 226, 152, 37,  179, 16,  145, 34,  136,
                54,  208, 148, 206, 143, 150, 219, 189, 241, 210, 19,  92,  131, 56,  70,  64,
                30,  66,  182, 163, 195, 72,  126, 110, 107, 58,  40,  84,  250, 133, 186, 61,
                202, 94,  155, 159, 10,  21,  121, 43,  78,  212, 229, 172, 115, 243, 167, 87,
                7,   112, 192, 247, 140, 128, 99,  13,  103, 74,  222, 237, 49,  197, 254, 24,
                227, 165, 153, 119, 38,  184, 180, 124, 17,  68,  146, 217, 35,  32,  137, 46,
                55,  63,  209, 91,  149, 188, 207, 205, 144, 135, 151, 178, 220, 252, 190, 97,
                242, 86,  211, 171, 20,  42,  93,  158, 132, 60,  57,  83,  71,  109, 65,  162,
                31,  45,  67,  216, 183, 123, 164, 118, 196, 23,  73,  236, 127, 12,  111, 246,
                108, 161, 59,  82,  41,  157, 85,  170, 251, 96,  134, 177, 187, 204, 62,  90,
                203, 89,  95,  176, 156, 169, 160, 81,  11,  245, 22,  235, 122, 117, 44,  215,
                79,  174, 213, 233, 230, 231, 173, 232, 116, 214, 244, 234, 168, 80,  88,  175
            };

        public static readonly byte[][] Polynomials = new byte[][]
        {
            new byte[] { },
            new byte[] { 0, 0, },
            new byte[] { 0, 25, 1, },
            new byte[] { 0, 198, 199, 3, },
            new byte[] { 0, 75, 249, 78, 6, },
            new byte[] { 0, 113, 164, 166, 119, 10, },
            new byte[] { 0, 166, 0, 134, 5, 176, 15, },
            new byte[] { 0, 87, 229, 146, 149, 238, 102, 21, },
            new byte[] { 0, 175, 238, 208, 249, 215, 252, 196, 28, },
            new byte[] { 0, 95, 246, 137, 231, 235, 149, 11, 123, 36, },
            new byte[] { 0, 251, 67, 46, 61, 118, 70, 64, 94, 32, 45, },
            new byte[] { 0, 220, 192, 91, 194, 172, 177, 209, 116, 227, 10, 55, },
            new byte[] { 0, 102, 43, 98, 121, 187, 113, 198, 143, 131, 87, 157, 66, },
            new byte[] { 0, 74, 152, 176, 100, 86, 100, 106, 104, 130, 218, 206, 140, 78, },
            new byte[] { 0, 199, 249, 155, 48, 190, 124, 218, 137, 216, 87, 207, 59, 22, 91, },
            new byte[] { 0, 8, 183, 61, 91, 202, 37, 51, 58, 58, 237, 140, 124, 5, 99, 105, },
            new byte[] { 0, 120, 104, 107, 109, 102, 161, 76, 3, 91, 191, 147, 169, 182, 194, 225, 120, },
            new byte[] { 0, 43, 139, 206, 78, 43, 239, 123, 206, 214, 147, 24, 99, 150, 39, 243, 163, 136, },
            new byte[] { 0, 215, 234, 158, 94, 184, 97, 118, 170, 79, 187, 152, 148, 252, 179, 5, 98, 96, 153, },
            new byte[] { 0, 67, 3, 105, 153, 52, 90, 83, 17, 150, 159, 44, 128, 153, 133, 252, 222, 138, 220, 171, },
            new byte[] { 0, 17, 60, 79, 50, 61, 163, 26, 187, 202, 180, 221, 225, 83, 239, 156, 164, 212, 212, 188, 190, },
            new byte[] { 0, 240, 233, 104, 247, 181, 140, 67, 98, 85, 200, 210, 115, 148, 137, 230, 36, 122, 254, 148, 175, 210, },
            new byte[] { 0, 210, 171, 247, 242, 93, 230, 14, 109, 221, 53, 200, 74, 8, 172, 98, 80, 219, 134, 160, 105, 165, 231, },
            new byte[] { 0, 171, 102, 146, 91, 49, 103, 65, 17, 193, 150, 14, 25, 183, 248, 94, 164, 224, 192, 1, 78, 56, 147, 253, },
            new byte[] { 0, 229, 121, 135, 48, 211, 117, 251, 126, 159, 180, 169, 152, 192, 226, 228, 218, 111, 0, 117, 232, 87, 96, 227, 21, },
            new byte[] { 0, 231, 181, 156, 39, 170, 26, 12, 59, 15, 148, 201, 54, 66, 237, 208, 99, 167, 144, 182, 95, 243, 129, 178, 252, 45, },
            new byte[] { 0, 173, 125, 158, 2, 103, 182, 118, 17, 145, 201, 111, 28, 165, 53, 161, 21, 245, 142, 13, 102, 48, 227, 153, 145, 218, 70, },
            new byte[] { 0, 79, 228, 8, 165, 227, 21, 180, 29, 9, 237, 70, 99, 45, 58, 138, 135, 73, 126, 172, 94, 216, 193, 157, 26, 17, 149, 96, },
            new byte[] { 0, 168, 223, 200, 104, 224, 234, 108, 180, 110, 190, 195, 147, 205, 27, 232, 201, 21, 43, 245, 87, 42, 195, 212, 119, 242, 37, 9, 123, },
            new byte[] { 0, 156, 45, 183, 29, 151, 219, 54, 96, 249, 24, 136, 5, 241, 175, 189, 28, 75, 234, 150, 148, 23, 9, 202, 162, 68, 250, 140, 24, 151, },
            new byte[] { 0, 41, 173, 145, 152, 216, 31, 179, 182, 50, 48, 110, 86, 239, 96, 222, 125, 42, 173, 226, 193, 224, 130, 156, 37, 251, 216, 238, 40, 192, 180, },
            new byte[] { 0, 20, 37, 252, 93, 63, 75, 225, 31, 115, 83, 113, 39, 44, 73, 122, 137, 118, 119, 144, 248, 248, 55, 1, 225, 105, 123, 183, 117, 187, 200, 210, },
            new byte[] { 0, 10, 6, 106, 190, 249, 167, 4, 67, 209, 138, 138, 32, 242, 123, 89, 27, 120, 185, 80, 156, 38, 69, 171, 60, 28, 222, 80, 52, 254, 185, 220, 241, },
            new byte[] { 0, 245, 231, 55, 24, 71, 78, 76, 81, 225, 212, 173, 37, 215, 46, 119, 229, 245, 167, 126, 72, 181, 94, 165, 210, 98, 125, 159, 184, 169, 232, 185, 231, 18, },
            new byte[] { 0, 111, 77, 146, 94, 26, 21, 108, 19, 105, 94, 113, 193, 86, 140, 163, 125, 58, 158, 229, 239, 218, 103, 56, 70, 114, 61, 183, 129, 167, 13, 98, 62, 129, 51, },
            new byte[] { 0, 7, 94, 143, 81, 247, 127, 202, 202, 194, 125, 146, 29, 138, 162, 153, 65, 105, 122, 116, 238, 26, 36, 216, 112, 125, 228, 15, 49, 8, 162, 30, 126, 111, 58, 85, },
            new byte[] { 0, 200, 183, 98, 16, 172, 31, 246, 234, 60, 152, 115, 0, 167, 152, 113, 248, 238, 107, 18, 63, 218, 37, 87, 210, 105, 177, 120, 74, 121, 196, 117, 251, 113, 233, 30, 120, },
            new byte[] { 0, 154, 75, 141, 180, 61, 165, 104, 232, 46, 227, 96, 178, 92, 135, 57, 162, 120, 194, 212, 174, 252, 183, 42, 35, 157, 111, 23, 133, 100, 8, 105, 37, 192, 189, 159, 19, 156, },
            new byte[] { 0, 159, 34, 38, 228, 230, 59, 243, 95, 49, 218, 176, 164, 20, 65, 45, 111, 39, 81, 49, 118, 113, 222, 193, 250, 242, 168, 217, 41, 164, 247, 177, 30, 238, 18, 120, 153, 60, 193, },
            new byte[] { 0, 81, 216, 174, 47, 200, 150, 59, 156, 89, 143, 89, 166, 183, 170, 152, 21, 165, 177, 113, 132, 234, 5, 154, 68, 124, 175, 196, 157, 249, 233, 83, 24, 153, 241, 126, 36, 116, 19, 231, },
            new byte[] { 0, 59, 116, 79, 161, 252, 98, 128, 205, 128, 161, 247, 57, 163, 56, 235, 106, 53, 26, 187, 174, 226, 104, 170, 7, 175, 35, 181, 114, 88, 41, 47, 163, 125, 134, 72, 20, 232, 53, 35, 15, },
            new byte[] { 0, 132, 167, 52, 139, 184, 223, 149, 92, 250, 18, 83, 33, 127, 109, 194, 7, 211, 242, 109, 66, 86, 169, 87, 96, 187, 159, 114, 172, 118, 208, 183, 200, 82, 179, 38, 39, 34, 242, 142, 147, 55, },
            new byte[] { 0, 250, 103, 221, 230, 25, 18, 137, 231, 0, 3, 58, 242, 221, 191, 110, 84, 230, 8, 188, 106, 96, 147, 15, 131, 139, 34, 101, 223, 39, 101, 213, 199, 237, 254, 201, 123, 171, 162, 194, 117, 50, 96, },
            new byte[] { 0, 96, 67, 3, 245, 217, 215, 33, 65, 240, 109, 144, 63, 21, 131, 38, 101, 153, 128, 55, 31, 237, 3, 94, 160, 20, 87, 77, 56, 191, 123, 207, 75, 82, 0, 122, 132, 101, 145, 215, 15, 121, 192, 138, },
            new byte[] { 0, 190, 7, 61, 121, 71, 246, 69, 55, 168, 188, 89, 243, 191, 25, 72, 123, 9, 145, 14, 247, 1, 238, 44, 78, 143, 62, 224, 126, 118, 114, 68, 163, 52, 194, 217, 147, 204, 169, 37, 130, 113, 102, 73, 181, },
            new byte[] { 0, 6, 172, 72, 250, 18, 171, 171, 162, 229, 187, 239, 4, 187, 11, 37, 228, 102, 72, 102, 22, 33, 73, 95, 99, 132, 1, 15, 89, 4, 112, 130, 95, 211, 235, 227, 58, 35, 88, 132, 23, 44, 165, 54, 187, 225, },
            new byte[] { 0, 112, 94, 88, 112, 253, 224, 202, 115, 187, 99, 89, 5, 54, 113, 129, 44, 58, 16, 135, 216, 169, 211, 36, 1, 4, 96, 60, 241, 73, 104, 234, 8, 249, 245, 119, 174, 52, 25, 157, 224, 43, 202, 223, 19, 82, 15, },
            new byte[] { 0, 76, 164, 229, 92, 79, 168, 219, 110, 104, 21, 220, 74, 19, 199, 195, 100, 93, 191, 43, 213, 72, 56, 138, 161, 125, 187, 119, 250, 189, 137, 190, 76, 126, 247, 93, 30, 132, 6, 58, 213, 208, 165, 224, 152, 133, 91, 61, },
            new byte[] { 0, 228, 25, 196, 130, 211, 146, 60, 24, 251, 90, 39, 102, 240, 61, 178, 63, 46, 123, 115, 18, 221, 111, 135, 160, 182, 205, 107, 206, 95, 150, 120, 184, 91, 21, 247, 156, 140, 238, 191, 11, 94, 227, 84, 50, 163, 39, 34, 108, },
            new byte[] { 0, 172, 121, 1, 41, 193, 222, 237, 64, 109, 181, 52, 120, 212, 226, 239, 245, 208, 20, 246, 34, 225, 204, 134, 101, 125, 206, 69, 138, 250, 0, 77, 58, 143, 185, 220, 254, 210, 190, 112, 88, 91, 57, 90, 109, 5, 13, 181, 25, 156, },
            new byte[] { 0, 232, 125, 157, 161, 164, 9, 118, 46, 209, 99, 203, 193, 35, 3, 209, 111, 195, 242, 203, 225, 46, 13, 32, 160, 126, 209, 130, 160, 242, 215, 242, 75, 77, 42, 189, 32, 113, 65, 124, 69, 228, 114, 235, 175, 124, 170, 215, 232, 133, 205, },
            new byte[] { 0, 213, 166, 142, 43, 10, 216, 141, 163, 172, 180, 102, 70, 89, 62, 222, 62, 42, 210, 151, 163, 218, 70, 77, 39, 166, 191, 114, 202, 245, 188, 183, 221, 75, 212, 27, 237, 127, 204, 235, 62, 190, 232, 18, 46, 171, 15, 98, 247, 66, 163, 0, },
            new byte[] { 0, 116, 50, 86, 186, 50, 220, 251, 89, 192, 46, 86, 127, 124, 19, 184, 233, 151, 215, 22, 14, 59, 145, 37, 242, 203, 134, 254, 89, 190, 94, 59, 65, 124, 113, 100, 233, 235, 121, 22, 76, 86, 97, 39, 242, 200, 220, 101, 33, 239, 254, 116, 51, },
            new byte[] { 0, 122, 214, 231, 136, 199, 11, 6, 205, 124, 72, 213, 117, 187, 60, 147, 201, 73, 75, 33, 146, 171, 247, 118, 208, 157, 177, 203, 235, 83, 45, 226, 202, 229, 168, 7, 57, 237, 235, 200, 124, 106, 254, 165, 14, 147, 0, 57, 42, 31, 178, 213, 173, 103, },
            new byte[] { 0, 183, 26, 201, 87, 210, 221, 113, 21, 46, 65, 45, 50, 238, 184, 249, 225, 102, 58, 209, 218, 109, 165, 26, 95, 184, 192, 52, 245, 35, 254, 238, 175, 172, 79, 123, 25, 122, 43, 120, 108, 215, 80, 128, 201, 235, 8, 153, 59, 101, 31, 198, 76, 31, 156, },
            new byte[] { 0, 38, 197, 123, 167, 16, 87, 178, 238, 227, 97, 148, 247, 26, 90, 228, 182, 236, 197, 47, 249, 36, 213, 54, 113, 181, 74, 177, 204, 155, 61, 47, 42, 0, 132, 144, 251, 200, 38, 38, 138, 54, 44, 64, 19, 22, 206, 16, 10, 228, 211, 161, 171, 44, 194, 210, },
            new byte[] { 0, 106, 120, 107, 157, 164, 216, 112, 116, 2, 91, 248, 163, 36, 201, 202, 229, 6, 144, 254, 155, 135, 208, 170, 209, 12, 139, 127, 142, 182, 249, 177, 174, 190, 28, 10, 85, 239, 184, 101, 124, 152, 206, 96, 23, 163, 61, 27, 196, 247, 151, 154, 202, 207, 20, 61, 10, },
            new byte[] { 0, 58, 140, 237, 93, 106, 61, 193, 2, 87, 73, 194, 215, 159, 163, 10, 155, 5, 121, 153, 59, 248, 4, 117, 22, 60, 177, 144, 44, 72, 228, 62, 1, 19, 170, 113, 158, 25, 175, 199, 139, 90, 1, 210, 7, 119, 154, 89, 159, 130, 122, 46, 147, 190, 135, 94, 68, 66, },
            new byte[] { 0, 82, 116, 26, 247, 66, 27, 62, 107, 252, 182, 200, 185, 235, 55, 251, 242, 210, 144, 154, 237, 176, 141, 192, 248, 152, 249, 206, 85, 253, 142, 65, 165, 125, 23, 24, 30, 122, 240, 214, 6, 129, 218, 29, 145, 127, 134, 206, 245, 117, 29, 41, 63, 159, 142, 233, 125, 148, 123, },
            new byte[] { 0, 57, 115, 232, 11, 195, 217, 3, 206, 77, 67, 29, 166, 180, 106, 118, 203, 17, 69, 152, 213, 74, 44, 49, 43, 98, 61, 253, 122, 14, 43, 209, 143, 9, 104, 107, 171, 224, 57, 254, 251, 226, 232, 221, 194, 240, 117, 161, 82, 178, 246, 178, 33, 50, 86, 215, 239, 180, 180, 181, },
            new byte[] { 0, 107, 140, 26, 12, 9, 141, 243, 197, 226, 197, 219, 45, 211, 101, 219, 120, 28, 181, 127, 6, 100, 247, 2, 205, 198, 57, 115, 219, 101, 109, 160, 82, 37, 38, 238, 49, 160, 209, 121, 86, 11, 124, 30, 181, 84, 25, 194, 87, 65, 102, 190, 220, 70, 27, 209, 16, 89, 7, 33, 240, },
            new byte[] { 0, 161, 244, 105, 115, 64, 9, 221, 236, 16, 145, 148, 34, 144, 186, 13, 20, 254, 246, 38, 35, 202, 72, 4, 212, 159, 211, 165, 135, 252, 250, 25, 87, 30, 120, 226, 234, 92, 199, 72, 7, 155, 218, 231, 44, 125, 178, 156, 174, 124, 43, 100, 31, 56, 101, 204, 64, 175, 225, 169, 146, 45, },
            new byte[] { 0, 65, 202, 113, 98, 71, 223, 248, 118, 214, 94, 0, 122, 37, 23, 2, 228, 58, 121, 7, 105, 135, 78, 243, 118, 70, 76, 223, 89, 72, 50, 70, 111, 194, 17, 212, 126, 181, 35, 221, 117, 235, 11, 229, 149, 147, 123, 213, 40, 115, 6, 200, 100, 26, 246, 182, 218, 127, 215, 36, 186, 110, 106, },
            new byte[] { 0, 30, 71, 36, 71, 19, 195, 172, 110, 61, 2, 169, 194, 90, 136, 59, 182, 231, 145, 102, 39, 170, 231, 214, 67, 196, 207, 53, 112, 246, 90, 90, 121, 183, 146, 74, 77, 38, 89, 22, 231, 55, 56, 242, 112, 217, 110, 123, 62, 201, 217, 128, 165, 60, 181, 37, 161, 246, 132, 246, 18, 115, 136, 168, },
            new byte[] { 0, 45, 51, 175, 9, 7, 158, 159, 49, 68, 119, 92, 123, 177, 204, 187, 254, 200, 78, 141, 149, 119, 26, 127, 53, 160, 93, 199, 212, 29, 24, 145, 156, 208, 150, 218, 209, 4, 216, 91, 47, 184, 146, 47, 140, 195, 195, 125, 242, 238, 63, 99, 108, 140, 230, 242, 31, 204, 11, 178, 243, 217, 156, 213, 231, },
            new byte[] { 0, 137, 158, 247, 240, 37, 238, 214, 128, 99, 218, 46, 138, 198, 128, 92, 219, 109, 139, 166, 25, 66, 67, 14, 58, 238, 149, 177, 195, 221, 154, 171, 48, 80, 12, 59, 190, 228, 19, 55, 208, 92, 112, 229, 37, 60, 10, 47, 81, 0, 192, 37, 171, 175, 147, 128, 73, 166, 61, 149, 12, 24, 95, 70, 113, 40, },
            new byte[] { 0, 5, 118, 222, 180, 136, 136, 162, 51, 46, 117, 13, 215, 81, 17, 139, 247, 197, 171, 95, 173, 65, 137, 178, 68, 111, 95, 101, 41, 72, 214, 169, 197, 95, 7, 44, 154, 77, 111, 236, 40, 121, 143, 63, 87, 80, 253, 240, 126, 217, 77, 34, 232, 106, 50, 168, 82, 76, 146, 67, 106, 171, 25, 132, 93, 45, 105, },
            new byte[] { 0, 191, 172, 113, 86, 7, 166, 246, 185, 155, 250, 98, 113, 89, 86, 214, 225, 156, 190, 58, 33, 144, 67, 179, 163, 52, 154, 233, 151, 104, 251, 160, 126, 175, 208, 225, 70, 227, 146, 4, 152, 139, 103, 25, 107, 61, 204, 159, 250, 193, 225, 105, 160, 98, 167, 2, 53, 16, 242, 83, 210, 196, 103, 248, 86, 211, 41, 171, },
            new byte[] { 0, 247, 159, 223, 33, 224, 93, 77, 70, 90, 160, 32, 254, 43, 150, 84, 101, 190, 205, 133, 52, 60, 202, 165, 220, 203, 151, 93, 84, 15, 84, 253, 173, 160, 89, 227, 52, 199, 97, 95, 231, 52, 177, 41, 125, 137, 241, 166, 225, 118, 2, 54, 32, 82, 215, 175, 198, 43, 238, 235, 27, 101, 184, 127, 3, 5, 8, 163, 238, },
        };
        #endregion

        private ModuleType[,] modules;
        private int[,] accessCount;
        private bool[,] freeMask;
        private int dim;
    }
}
