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
        {
            Type = type;
            ErrorCorrection = errorCorrection;
            
            var bits = Encode(data);
            Prep();
            Fill(bits);
            var mask = Mask();
            AddFormatInformation(mask);
            AddVersionInformation();
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
                    if (accessCount[qz+x, qz+y] == 0)
                        Console.Write("_");
                    else if (modules[qz+x, qz+y] == ModuleType.Dark)
                        Console.Write("#");
                    else
                        Console.Write(".");
                }
                Console.WriteLine();
            }
        }

        public void Save(string path, int scale)
        {
            using (Bitmap b = new Bitmap(fullDim * scale, fullDim * scale))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.White);

                    for (int x = 0; x < dim; x++)
                    {
                        for (int y= 0 ; y < dim; y++)
                        {
                            if (Get(x, y) == ModuleType.Dark)
                            {
                                g.FillRectangle(Brushes.Black, x * scale, y * scale, scale, scale);
                            }
                        }
                    }
                }

                b.Save(path);
            }
        }

        #region Steps
        private BitArray Encode(string data)
        {
            int idx, i;

            #region Mode and Version choice
            var mode = Mode.Byte;

            if (data.All(c => Char.IsDigit(c)))
            {
                mode = Mode.Numeric;
                Version = DataCapacityTable[Type][ErrorCorrection].First(p => p.Value.Item2 >= data.Length).Key;
            }
            else if (data.All(c => AlphaNumericTable.ContainsKey(c)))
            {
                mode = Mode.AlphaNumeric;
                Version = DataCapacityTable[Type][ErrorCorrection].First(p => p.Value.Item3 >= data.Length).Key;
            }
            else
            {
                mode = Mode.Byte;
                Version = DataCapacityTable[Type][ErrorCorrection].First(p => p.Value.Item4 >= data.Length).Key;
            }
            #endregion

            #region Code word creation
            // encode data as series of bit arrays
            List<BitArray> bits = new List<BitArray>();
            bits.Add(EncodeMode(mode));
            bits.Add(EncodeCharacterCount(mode, data.Length));

            switch (mode)
            {
                case Mode.Byte:
                    var bytes = Encoding.UTF8.GetBytes(data);
                    foreach (var by in bytes)
                    {
                        var b = new BitArray(8, false);
                        for (i = 0; i < 8; i++)
                            if ((by & (0x80 >> i)) != 0)
                                b[i] = true;
                        bits.Add(b);
                    }
                    break;

                case Mode.Numeric:
                    for (idx = 0; idx < data.Length - 2; idx += 3)
                    {
                        int x = AlphaNumericTable[data[idx]] * 100 + AlphaNumericTable[data[idx + 1]] * 10 + AlphaNumericTable[data[idx + 2]];
                        var b = new BitArray(10, false);
                        for (i = 0; i < 10; i++)
                            if ((x & (0x200 >> i)) != 0)
                                b[i] = true;
                        bits.Add(b);
                    }

                    if (idx < data.Length - 1)
                    {
                        int x = AlphaNumericTable[data[idx]] * 10 + AlphaNumericTable[data[idx + 1]];
                        idx += 2;
                        var b = new BitArray(7, false);
                        for (i = 0; i < 7; i++)
                            if ((x & (0x40 >> i)) != 0)
                                b[i] = true;
                        bits.Add(b);
                    }

                    if (idx < data.Length)
                    {
                        int x = AlphaNumericTable[data[idx]];
                        idx += 1;
                        var b = new BitArray(4, false);
                        for (i = 0; i < 4; i++)
                            if ((x & (0x08 >> i)) != 0)
                                b[i] = true;
                        bits.Add(b);
                    }
                    break;

                case Mode.AlphaNumeric:
                    for (idx = 0; idx < data.Length - 1; idx += 2)
                    {
                        int x = AlphaNumericTable[data[idx]] * 45 + AlphaNumericTable[data[idx + 1]];
                        var b = new BitArray(11, false);
                        for (i = 0; i < 11; i++)
                            if ((x & (0x400 >> i)) != 0)
                                b[i] = true;
                        bits.Add(b);
                    }

                    if (idx < data.Length)
                    {
                        int x = AlphaNumericTable[data[idx]];
                        var b = new BitArray(6, false);
                        for (i = 0; i < 6; i++)
                            if ((x & (0x20 >> i)) != 0)
                                b[i] = true;
                        bits.Add(b);
                    }
                    break;
            }
            
            bits.Add(EncodeMode(Mode.Terminator));

            int bitstreamLength = bits.Sum(b => b.Length);

            // get capacity of symbol
            int capacity = DataCapacityTable[Type][ErrorCorrection][Version].Item1 * 8;

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
            i = 0;
            while (bitstreamLength < (capacity - 4))
            {
                bits.Add(new BitArray(new byte[] { padCodewords[i] }));
                bitstreamLength += 8;
                i = (i + 1) % 2;
            }

            // fill the last nibble with zeroes (only necessary for M1 and M3)
            if (bitstreamLength < capacity)
            {
                bits.Add(new BitArray(4));
                bitstreamLength += 4;
            }

            // flatten list of bitarrays
            bool[] flattenedBits = new bool[bitstreamLength];
            idx = 0;
            foreach (var b in bits)
            {
                b.CopyTo(flattenedBits, idx);
                idx += b.Length;
            }

            // convert to code words
            byte[] codeWords = new byte[(flattenedBits.Length - 1) / 8 + 1];
            for (int b = 0; b < flattenedBits.Length; b++)
            {
                if (flattenedBits[b])
                {
                    codeWords[b / 8] |= (byte)(0x80 >> (b % 8));
                }
            }
            #endregion

            #region Error word calculation
            List<byte[]> dataBlocks = new List<byte[]>();
            List<byte[]> eccBlocks = new List<byte[]>();

            // generate error correction words
            var ecc = ErrorCorrectionTable[Type][Version][ErrorCorrection];
            idx = 0;

            foreach (var e in ecc)
            {
                int dataWords = e.Item3;
                int errorWords = e.Item2 - e.Item3;

                var poly = Polynomials[errorWords].ToArray();

                // for each block of that structure
                for (int b = 0; b < e.Item1; b++)
                {
                    // add the data block to the list
                    dataBlocks.Add(codeWords.Skip(idx).Take(dataWords).ToArray());
                    idx += dataWords;

                    // pad the block with zeroes
                    var temp = Enumerable.Concat(dataBlocks.Last(), Enumerable.Repeat((byte)0, errorWords)).ToArray();

                    // perform polynomial division to calculate error block
                    for (int start = 0; start < dataWords; start++)
                    {
                        byte pow = LogTable[temp[start]];
                        for (i = 0; i < poly.Length; i++)
                            temp[i + start] ^= ExponentTable[Mul(poly[i], pow)];
                    }

                    // add error block to the list
                    eccBlocks.Add(temp.Skip(dataWords).ToArray());
                }
            }
            #endregion

            // generate final data sequence
            byte[] sequence = new byte[dataBlocks.Sum(b => b.Length) + eccBlocks.Sum(b => b.Length)];
            idx = 0;
            for (i = 0; i < dataBlocks.Max(b => b.Length); i++)
            {
                foreach (var b in dataBlocks.Where(b => b.Length > i))
                {
                    sequence[idx++] = b[i];
                }
            }
            for (i = 0; i < eccBlocks.Max(b => b.Length); i++)
            {
                foreach (var b in eccBlocks.Where(b => b.Length > i))
                {
                    sequence[idx++] = b[i];
                }
            }

            return new BitArray(sequence);
        }

        private void Prep()
        {
            dim = GetSymbolDimension();
            qz = GetQuietZoneDimension();
            fullDim = dim + 2 * qz;

            // initialize to a full symbol of unaccessed, light modules
            freeMask = new bool[fullDim, fullDim];
            accessCount = new int[fullDim, fullDim];
            modules = new ModuleType[fullDim, fullDim];
            for (int x = 0; x < fullDim; x++)
            {
                for (int y = 0; y < fullDim; y++)
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

            CreateMask();
        }

        private void Fill(BitArray bits)
        {
            // fill with data
            int idx = 0;
            bool up = true;
            for (int x = dim - 1; x >= 0; x -= 2)
            {
                // skip over the vertical timing line
                if (x == 6)
                    x--;

                for (int y = (up ? dim - 1 : 0); y >= 0 && y < dim; y += (up ? -1 : 1))
                {
                    for (int dx = 0; dx > -2; dx--)
                    {
                        if (IsFree(x + dx, y))
                        {
                            if (idx < bits.Length)
                                Set(x + dx, y, bits[(idx / 8 * 8) + (7 - (idx % 8))] ? ModuleType.Dark : ModuleType.Light);
                            else
                                Set(x + dx, y, ModuleType.Light);
                            idx++;
                        }
                    }
                }

                up = !up;
            }
        }

        private byte Mask()
        {
            List<Tuple<byte, byte, Func<int, int, bool>>> masks = null;
            
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
            var winner = results.OrderBy(t => t.Item2).First().Item1;

            // apply the winner
            Apply(winner.Item3);

            return Type == SymbolType.Normal ? winner.Item1 : winner.Item2;
        }

        private void AddFormatInformation(byte maskID)
        {
            if (Type == SymbolType.Micro)
                return;

            var bits = FormatStrings[ErrorCorrection][maskID];

            // add format information around top-left finder pattern
            Set(8, 0, bits[14] ? ModuleType.Dark : ModuleType.Light);
            Set(8, 1, bits[13] ? ModuleType.Dark : ModuleType.Light);
            Set(8, 2, bits[12] ? ModuleType.Dark : ModuleType.Light);
            Set(8, 3, bits[11] ? ModuleType.Dark : ModuleType.Light);
            Set(8, 4, bits[10] ? ModuleType.Dark : ModuleType.Light);
            Set(8, 5, bits[ 9] ? ModuleType.Dark : ModuleType.Light);
            Set(8, 7, bits[ 8] ? ModuleType.Dark : ModuleType.Light);
            Set(8, 8, bits[ 7] ? ModuleType.Dark : ModuleType.Light);
            Set(7, 8, bits[ 6] ? ModuleType.Dark : ModuleType.Light);
            Set(5, 8, bits[ 5] ? ModuleType.Dark : ModuleType.Light);
            Set(4, 8, bits[ 4] ? ModuleType.Dark : ModuleType.Light);
            Set(3, 8, bits[ 3] ? ModuleType.Dark : ModuleType.Light);
            Set(2, 8, bits[ 2] ? ModuleType.Dark : ModuleType.Light);
            Set(1, 8, bits[ 1] ? ModuleType.Dark : ModuleType.Light);
            Set(0, 8, bits[ 0] ? ModuleType.Dark : ModuleType.Light);

            // add format information around top-right finder pattern
            Set(dim - 1, 8, bits[14] ? ModuleType.Dark : ModuleType.Light);
            Set(dim - 2, 8, bits[13] ? ModuleType.Dark : ModuleType.Light);
            Set(dim - 3, 8, bits[12] ? ModuleType.Dark : ModuleType.Light);
            Set(dim - 4, 8, bits[11] ? ModuleType.Dark : ModuleType.Light);
            Set(dim - 5, 8, bits[10] ? ModuleType.Dark : ModuleType.Light);
            Set(dim - 6, 8, bits[ 9] ? ModuleType.Dark : ModuleType.Light);
            Set(dim - 7, 8, bits[ 8] ? ModuleType.Dark : ModuleType.Light);
            Set(dim - 8, 8, bits[ 7] ? ModuleType.Dark : ModuleType.Light);

            // add format information around bottom-left finder pattern
            Set(8, dim - 7, bits[ 6] ? ModuleType.Dark : ModuleType.Light);
            Set(8, dim - 6, bits[ 5] ? ModuleType.Dark : ModuleType.Light);
            Set(8, dim - 5, bits[ 4] ? ModuleType.Dark : ModuleType.Light);
            Set(8, dim - 4, bits[ 3] ? ModuleType.Dark : ModuleType.Light);
            Set(8, dim - 3, bits[ 2] ? ModuleType.Dark : ModuleType.Light);
            Set(8, dim - 2, bits[ 1] ? ModuleType.Dark : ModuleType.Light);
            Set(8, dim - 1, bits[ 0] ? ModuleType.Dark : ModuleType.Light);
        }

        private void AddVersionInformation()
        {
            if (Type == SymbolType.Micro || Version < 7)
                return;

            var bits = VersionStrings[Version];

            // write top-right block
            var idx = 17;
            for (int y = 0; y < 6; y++)
            {
                for (int x = dim - 11; x < dim - 8; x++)
                {
                    Set(x, y, bits[idx--] ? ModuleType.Dark : ModuleType.Light);
                }
            }

            // write bottom-left block
            idx = 17;
            for (int x = 0; x < 6; x++)
            {
                for (int y = dim - 11; y < dim - 8; y++)
                {
                    Set(x, y, bits[idx--] ? ModuleType.Dark : ModuleType.Light);
                }
            }
        }
        #endregion

        #region Drawing Helpers
        private void DrawFinderPattern(int centerX, int centerY)
        {
            DrawRect(centerX-3, centerY-3, 7, 7, ModuleType.Dark);
            DrawRect(centerX-2, centerY-2, 5, 5, ModuleType.Light);
            FillRect(centerX-1, centerY-1, 3, 3, ModuleType.Dark);
        }

        private void DrawAlignmentPattern(int centerX, int centerY)
        {
            DrawRect(centerX-2, centerY-2, 5, 5, ModuleType.Dark);
            DrawRect(centerX-1, centerY-1, 3, 3, ModuleType.Light);
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
            {
                Set(x + dx, y, ((x+dx) % 2 == 0) ? ModuleType.Dark : ModuleType.Light);
            }
        }

        private void DrawTimingVLine(int x, int y, int length)
        {
            for (int dy = 0; dy < length; dy++)
            {
                Set(x, y + dy, ((y+dy) % 2 == 0) ? ModuleType.Dark : ModuleType.Light);
            }
        }

        private void Set(int x, int y, ModuleType type)
        {
            modules[qz + x, qz + y] = type;
            accessCount[qz + x, qz + y]++;
        }

        private ModuleType Get(int x, int y)
        {
            return modules[qz + x, qz + y];
        }

        private void CreateMask()
        {
            for (int x = 0; x < dim; x++)
            {
                for (int y = 0; y < dim; y++)
                {
                    freeMask[qz + x, qz + y] = accessCount[qz + x, qz + y] == 0;
                }
            }
        }

        private bool IsFree(int x, int y)
        {
            return freeMask[qz + x, qz + y];
        }
        #endregion

        #region Masking Helpers
        private int EvaluateMask(Func<int, int, bool> mask)
        {
            // apply the mask
            Apply(mask);

            try
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
                        penalty += 5 + count;
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
                                penalty += 5 + count;

                            last = m;
                            count = 1;
                        }
                    }

                    if (count >= 5)
                        penalty += 5 + count;
                }

                // block penalties
                for (int x = 0; x < dim - 1; x++)
                {
                    for (int y = 0; y < dim - 1; y++)
                    {
                        var m = Get(x, y);

                        if (m == Get(x+1, y) && m == Get(x, y+1) && m == Get(x+1, y+1))
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
                int up   = (percentDark % 5 == 0) ? percentDark : percentDark + (5 - (percentDark % 5));
                int down = (percentDark % 5 == 0) ? percentDark : percentDark - (percentDark % 5);
                up = Math.Abs(up - 50);
                down = Math.Abs(down - 50);
                up /= 5;
                down /= 5;
                penalty += Math.Min(up, down) * 10;

                return penalty;
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

        private int GetQuietZoneDimension()
        {
            return 0;

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
                result.Set(i, (count & ((1 << (bits - 1)) >> i)) != 0);
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
        private static Dictionary<char, int> AlphaNumericTable =
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

        private static int[][] AlignmentPatternLocations = 
            new int[][]
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

        private static Mode[] NormalModes =
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

        private static Dictionary<int, Mode[]> MicroModes = 
            new Dictionary<int, Mode[]>
            {
                { 1, new Mode[] { Mode.Numeric, Mode.Terminator } },
                { 2, new Mode[] { Mode.Numeric, Mode.AlphaNumeric, Mode.Terminator } },
                { 3, new Mode[] { Mode.Numeric, Mode.AlphaNumeric, Mode.Byte, Mode.Kanji, Mode.Terminator } },
                { 4, new Mode[] { Mode.Numeric, Mode.AlphaNumeric, Mode.Byte, Mode.Kanji, Mode.Terminator } },
            };

        private static Dictionary<Mode, BitArray> NormalModeEncodings = 
            new Dictionary<Mode, BitArray>()
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

        private static Dictionary<ErrorCorrection, byte> ErrorCorrectionEncodings = 
            new Dictionary<ErrorCorrection, byte>()
            {
                { ErrorCorrection.L, 1 },
                { ErrorCorrection.M, 0 },
                { ErrorCorrection.Q, 3 },
                { ErrorCorrection.H, 2 }
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
                                                                                Tuple.Create( 2,  59,  37,  11) }),
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

        private static Dictionary<SymbolType, Dictionary<ErrorCorrection, Dictionary<int, Tuple<int, int, int, int, int>>>> DataCapacityTable =
            new List<Tuple<SymbolType, int, ErrorCorrection, Tuple<int, int, int, int, int>>>()
            {
                Tuple.Create(SymbolType.Micro,   2, ErrorCorrection.L, Tuple.Create(   5,   10,    6,    0,    0)),
                Tuple.Create(SymbolType.Micro,   2, ErrorCorrection.M, Tuple.Create(   4,    8,    5,    0,    0)),
                Tuple.Create(SymbolType.Micro,   3, ErrorCorrection.L, Tuple.Create(  11,   23,   14,    9,    6)),
                Tuple.Create(SymbolType.Micro,   3, ErrorCorrection.M, Tuple.Create(   9,   18,   11,    7,    4)),
                Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.L, Tuple.Create(  16,   35,   21,   15,    9)),
                Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.M, Tuple.Create(  14,   30,   18,   13,    8)),
                Tuple.Create(SymbolType.Micro,   4, ErrorCorrection.Q, Tuple.Create(  10,   21,   13,    9,    5)),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.L, Tuple.Create(  19,   41,   25,   15,    9)),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.M, Tuple.Create(  16,   34,   20,   14,    8)),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.Q, Tuple.Create(  13,   27,   16,   11,    7)),
                Tuple.Create(SymbolType.Normal,  1, ErrorCorrection.H, Tuple.Create(   9,   17,   10,    7,    4)),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.L, Tuple.Create(  34,   77,   47,   32,   20)),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.M, Tuple.Create(  28,   63,   38,   26,   16)),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.Q, Tuple.Create(  22,   48,   29,   20,   12)),
                Tuple.Create(SymbolType.Normal,  2, ErrorCorrection.H, Tuple.Create(  16,   34,   20,   14,    8)),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.L, Tuple.Create(  55,  127,   77,   53,   32)),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.M, Tuple.Create(  44,  101,   61,   42,   26)),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.Q, Tuple.Create(  34,   77,   47,   32,   20)),
                Tuple.Create(SymbolType.Normal,  3, ErrorCorrection.H, Tuple.Create(  26,   58,   35,   24,   15)),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.L, Tuple.Create(  80,  187,  114,   78,   48)),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.M, Tuple.Create(  64,  149,   90,   62,   38)),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.Q, Tuple.Create(  48,  111,   67,   46,   28)),
                Tuple.Create(SymbolType.Normal,  4, ErrorCorrection.H, Tuple.Create(  36,   82,   50,   34,   21)),
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.L, Tuple.Create( 108,  255,  154,  106,   65)),
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.M, Tuple.Create(  86,  202,  122,   84,   52)),
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.Q, Tuple.Create(  62,  144,   87,   60,   37)), 
                Tuple.Create(SymbolType.Normal,  5, ErrorCorrection.H, Tuple.Create(  46,  106,   64,   44,   27)), 
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.L, Tuple.Create( 136,  322,  195,  134,   82)),
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.M, Tuple.Create( 108,  255,  154,  106,   65)),
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.Q, Tuple.Create(  76,  178,  108,   74,   45)),
                Tuple.Create(SymbolType.Normal,  6, ErrorCorrection.H, Tuple.Create(  60,  139,   84,   58,   36)),
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.L, Tuple.Create( 156,  370,  224,  154,   95)),
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.M, Tuple.Create( 124,  293,  178,  122,   75)),
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.Q, Tuple.Create(  88,  207,  125,   86,   53)), 
                Tuple.Create(SymbolType.Normal,  7, ErrorCorrection.H, Tuple.Create(  66,  154,   93,   64,   39)), 
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.L, Tuple.Create( 194,  461,  279,  192,  118)),
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.M, Tuple.Create( 154,  365,  221,  152,   93)),
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.Q, Tuple.Create( 110,  259,  157,  108,   66)), 
                Tuple.Create(SymbolType.Normal,  8, ErrorCorrection.H, Tuple.Create(  86,  202,  122,   84,   52)), 
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.L, Tuple.Create( 232,  552,  335,  230,  141)),
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.M, Tuple.Create( 182,  432,  262,  180,  111)),
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.Q, Tuple.Create( 132,  312,  189,  130,   80)), 
                Tuple.Create(SymbolType.Normal,  9, ErrorCorrection.H, Tuple.Create( 100,  235,  143,   98,   60)), 
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.L, Tuple.Create( 274,  652,  395,  271,  167)),
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.M, Tuple.Create( 216,  513,  311,  213,  131)),
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.Q, Tuple.Create( 154,  364,  221,  151,   93)), 
                Tuple.Create(SymbolType.Normal, 10, ErrorCorrection.H, Tuple.Create( 122,  288,  174,  119,   74)), 
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.L, Tuple.Create( 324,  772,  468,  321,  198)),
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.M, Tuple.Create( 254,  604,  366,  251,  155)),
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.Q, Tuple.Create( 180,  427,  259,  177,  109)), 
                Tuple.Create(SymbolType.Normal, 11, ErrorCorrection.H, Tuple.Create( 140,  331,  200,  137,   85)), 
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.L, Tuple.Create( 370,  883,  535,  367,  226)),
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.M, Tuple.Create( 290,  691,  419,  287,  177)),
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.Q, Tuple.Create( 206,  489,  296,  203,  125)), 
                Tuple.Create(SymbolType.Normal, 12, ErrorCorrection.H, Tuple.Create( 158,  374,  227,  155,   96)), 
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.L, Tuple.Create( 428, 1022,  619,  425,  262)),
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.M, Tuple.Create( 334,  796,  483,  331,  204)),
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.Q, Tuple.Create( 244,  580,  352,  241,  149)), 
                Tuple.Create(SymbolType.Normal, 13, ErrorCorrection.H, Tuple.Create( 180,  427,  259,  177,  109)), 
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.L, Tuple.Create( 461, 1101,  667,  458,  282)),
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.M, Tuple.Create( 365,  871,  528,  362,  223)),
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.Q, Tuple.Create( 291,  621,  376,  258,  159)), 
                Tuple.Create(SymbolType.Normal, 14, ErrorCorrection.H, Tuple.Create( 197,  468,  283,  194,  120)), 
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.L, Tuple.Create( 523, 1250,  758,  520,  320)),
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.M, Tuple.Create( 415,  991,  600,  412,  254)),
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.Q, Tuple.Create( 295,  703,  426,  292,  180)), 
                Tuple.Create(SymbolType.Normal, 15, ErrorCorrection.H, Tuple.Create( 223,  530,  321,  220,  136)), 
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.L, Tuple.Create( 589, 1408,  854,  586,  361)),
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.M, Tuple.Create( 453, 1082,  656,  450,  277)),
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.Q, Tuple.Create( 325,  775,  470,  322,  198)), 
                Tuple.Create(SymbolType.Normal, 16, ErrorCorrection.H, Tuple.Create( 253,  602,  365,  250,  154)), 
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.L, Tuple.Create( 647, 1548,  938,  644,  397)),
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.M, Tuple.Create( 507, 1212,  734,  504,  310)),
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.Q, Tuple.Create( 367,  876,  531,  364,  224)), 
                Tuple.Create(SymbolType.Normal, 17, ErrorCorrection.H, Tuple.Create( 283,  674,  408,  280,  173)), 
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.L, Tuple.Create( 721, 1725, 1046,  718,  442)),
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.M, Tuple.Create( 563, 1346,  816,  560,  345)),
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.Q, Tuple.Create( 397,  948,  574,  394,  243)), 
                Tuple.Create(SymbolType.Normal, 18, ErrorCorrection.H, Tuple.Create( 313,  746,  452,  310,  191)), 
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.L, Tuple.Create( 795, 1903, 1153,  792,  488)),
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.M, Tuple.Create( 627, 1500,  909,  624,  384)),
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.Q, Tuple.Create( 445, 1063,  644,  442,  272)), 
                Tuple.Create(SymbolType.Normal, 19, ErrorCorrection.H, Tuple.Create( 341,  813,  493,  338,  208)), 
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.L, Tuple.Create( 861, 2061, 1249,  858,  528)),
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.M, Tuple.Create( 669, 1600,  970,  666,  410)),
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.Q, Tuple.Create( 445, 1159,  702,  482,  297)), 
                Tuple.Create(SymbolType.Normal, 20, ErrorCorrection.H, Tuple.Create( 341,  919,  557,  382,  235)), 
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.L, Tuple.Create( 932, 2232, 1352,  929,  572)),
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.M, Tuple.Create( 714, 1708, 1035,  711,  438)),
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.Q, Tuple.Create( 512, 1224,  742,  509,  314)), 
                Tuple.Create(SymbolType.Normal, 21, ErrorCorrection.H, Tuple.Create( 406,  969,  587,  403,  248)), 
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.L, Tuple.Create(1006, 2409, 1460, 1003,  618)),
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.M, Tuple.Create( 782, 1872, 1134,  779,  480)),
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.Q, Tuple.Create( 568, 1358,  823,  565,  348)), 
                Tuple.Create(SymbolType.Normal, 22, ErrorCorrection.H, Tuple.Create( 442, 1056,  640,  439,  270)),
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.L, Tuple.Create(1094, 2620, 1588, 1091,  672)),
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.M, Tuple.Create( 860, 2059, 1248,  857,  528)),
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.Q, Tuple.Create( 614, 1468,  890,  611,  376)), 
                Tuple.Create(SymbolType.Normal, 23, ErrorCorrection.H, Tuple.Create( 464, 1108,  672,  461,  284)), 
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.L, Tuple.Create(1174, 2812, 1704, 1171,  721)),
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.M, Tuple.Create( 914, 2188, 1326,  911,  561)),
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.Q, Tuple.Create( 664, 1588,  963,  661,  407)), 
                Tuple.Create(SymbolType.Normal, 24, ErrorCorrection.H, Tuple.Create( 514, 1228,  744,  511,  315)), 
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.L, Tuple.Create(1276, 3057, 1853, 1273,  784)),
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.M, Tuple.Create(1000, 2395, 1451,  997,  614)),
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.Q, Tuple.Create( 718, 1718, 1041,  715,  440)), 
                Tuple.Create(SymbolType.Normal, 25, ErrorCorrection.H, Tuple.Create( 538, 1286,  779,  535,  330)), 
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.L, Tuple.Create(1370, 3283, 1990, 1367,  842)),
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.M, Tuple.Create(1062, 2544, 1542, 1059,  652)),
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.Q, Tuple.Create( 754, 1804, 1094,  751,  462)), 
                Tuple.Create(SymbolType.Normal, 26, ErrorCorrection.H, Tuple.Create( 596, 1425,  864,  593,  365)), 
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.L, Tuple.Create(1468, 3517, 2132, 1465,  902)),
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.M, Tuple.Create(1128, 2701, 1637, 1125,  692)),
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.Q, Tuple.Create( 808, 1933, 1172,  805,  496)), 
                Tuple.Create(SymbolType.Normal, 27, ErrorCorrection.H, Tuple.Create( 628, 1501,  910,  625,  385)), 
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.L, Tuple.Create(1531, 3669, 2223, 1528,  940)),
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.M, Tuple.Create(1193, 2857, 1732, 1190,  732)),
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.Q, Tuple.Create( 871, 2085, 1263,  868,  534)), 
                Tuple.Create(SymbolType.Normal, 28, ErrorCorrection.H, Tuple.Create( 661, 1581,  958,  658,  405)), 
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.L, Tuple.Create(1631, 3909, 2369, 1628, 1002)),
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.M, Tuple.Create(1267, 3035, 1839, 1264,  778)),
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.Q, Tuple.Create( 911, 2181, 1322,  908,  559)), 
                Tuple.Create(SymbolType.Normal, 29, ErrorCorrection.H, Tuple.Create( 701, 1677, 1016,  698,  430)), 
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.L, Tuple.Create(1735, 4158, 2520, 1732, 1066)),
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.M, Tuple.Create(1373, 3289, 1994, 1370,  843)),
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.Q, Tuple.Create( 985, 2358, 1429,  982,  604)), 
                Tuple.Create(SymbolType.Normal, 30, ErrorCorrection.H, Tuple.Create( 745, 1782, 1080,  742,  457)), 
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.L, Tuple.Create(1843, 4417, 2677, 1840, 1132)),
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.M, Tuple.Create(1455, 3486, 2113, 1452,  894)),
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.Q, Tuple.Create(1033, 2473, 1499, 1030,  634)), 
                Tuple.Create(SymbolType.Normal, 31, ErrorCorrection.H, Tuple.Create( 793, 1897, 1150,  790,  486)), 
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.L, Tuple.Create(1955, 4686, 2840, 1952, 1201)),
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.M, Tuple.Create(1541, 3693, 2238, 1538,  947)),
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.Q, Tuple.Create(1115, 2670, 1618, 1112,  684)), 
                Tuple.Create(SymbolType.Normal, 32, ErrorCorrection.H, Tuple.Create( 845, 2022, 1226,  842,  518)), 
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.L, Tuple.Create(2071, 4965, 3009, 2068, 1273)),
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.M, Tuple.Create(1631, 3909, 2369, 1628, 1002)),
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.Q, Tuple.Create(1171, 2805, 1700, 1168,  719)), 
                Tuple.Create(SymbolType.Normal, 33, ErrorCorrection.H, Tuple.Create( 901, 2157, 1307,  898,  553)), 
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.L, Tuple.Create(2191, 5253, 3183, 2188, 1347)),
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.M, Tuple.Create(1725, 4134, 2506, 1722, 1060)),
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.Q, Tuple.Create(1231, 2949, 1787, 1228,  756)), 
                Tuple.Create(SymbolType.Normal, 34, ErrorCorrection.H, Tuple.Create( 961, 2301, 1394,  958,  590)), 
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.L, Tuple.Create(2306, 5529, 3351, 2303, 1417)),
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.M, Tuple.Create(1812, 4343, 2632, 1809, 1113)),
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.Q, Tuple.Create(1286, 3081, 1867, 1283,  790)), 
                Tuple.Create(SymbolType.Normal, 35, ErrorCorrection.H, Tuple.Create( 986, 2361, 1431,  983,  605)), 
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.L, Tuple.Create(2434, 5836, 3537, 2431, 1496)),
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.M, Tuple.Create(1914, 4588, 2780, 1911, 1176)),
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.Q, Tuple.Create(1354, 3244, 1966, 1351,  832)), 
                Tuple.Create(SymbolType.Normal, 36, ErrorCorrection.H, Tuple.Create(1054, 2524, 1530, 1051,  647)), 
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.L, Tuple.Create(2566, 6153, 3729, 2563, 1577)),
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.M, Tuple.Create(1992, 4775, 2894, 1989, 1224)),
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.Q, Tuple.Create(1426, 3417, 2071, 1423,  876)), 
                Tuple.Create(SymbolType.Normal, 37, ErrorCorrection.H, Tuple.Create(1096, 2625, 1591, 1093,  673)), 
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.L, Tuple.Create(2702, 6479, 3927, 2699, 1661)),
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.M, Tuple.Create(2102, 5039, 3054, 2099, 1292)),
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.Q, Tuple.Create(1502, 3599, 2181, 1499,  923)), 
                Tuple.Create(SymbolType.Normal, 38, ErrorCorrection.H, Tuple.Create(1142, 2735, 1658, 1139,  701)), 
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.L, Tuple.Create(2812, 6743, 4087, 2809, 1729)),
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.M, Tuple.Create(2216, 5313, 3220, 2213, 1362)),
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.Q, Tuple.Create(1582, 3791, 2298, 1579, 972)), 
                Tuple.Create(SymbolType.Normal, 39, ErrorCorrection.H, Tuple.Create(1222, 2927, 1774, 1219,  750)), 
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.L, Tuple.Create(2956, 7089, 4296, 2953, 1817)),
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.M, Tuple.Create(2334, 5596, 3391, 2331, 1435)),
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.Q, Tuple.Create(1666, 3993, 2420, 1663, 1024)), 
                Tuple.Create(SymbolType.Normal, 40, ErrorCorrection.H, Tuple.Create(1276, 3057, 1852, 1273,  784)), 
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

        private static Tuple<byte, byte, Func<int, int, bool>>[] DataMaskTable =
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

        private static Dictionary<ErrorCorrection, Dictionary<byte, BitArray>> FormatStrings = 
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
            }
            .GroupBy(t => t.Item1)
            .Select(t =>
                Tuple.Create(
                    t.Key,
                    t.ToDictionary(b => b.Item2, b => b.Item3)))
            .ToDictionary(t => t.Item1, t => t.Item2);

        private static Dictionary<int, BitArray> VersionStrings =
            new Dictionary<int, BitArray>()
            {
                {  7, new BitArray(new bool[] { false, false, false,  true,  true,  true,  true,  true, false, false,  true, false, false,  true, false,  true, false, false,  }) },
                {  8, new BitArray(new bool[] { false, false,  true, false, false, false, false,  true, false,  true,  true, false,  true,  true,  true,  true, false, false,  }) },
                {  9, new BitArray(new bool[] { false, false,  true, false, false,  true,  true, false,  true, false,  true, false, false,  true,  true, false, false,  true,  }) },
                { 10, new BitArray(new bool[] { false, false,  true, false,  true, false, false,  true, false, false,  true,  true, false,  true, false, false,  true,  true,  }) },
                { 11, new BitArray(new bool[] { false, false,  true, false,  true,  true,  true, false,  true,  true,  true,  true,  true,  true, false,  true,  true, false,  }) },
                { 12, new BitArray(new bool[] { false, false,  true,  true, false, false, false,  true,  true,  true, false,  true,  true, false, false, false,  true, false,  }) },
                { 13, new BitArray(new bool[] { false, false,  true,  true, false,  true,  true, false, false, false, false,  true, false, false, false,  true,  true,  true,  }) },
                { 14, new BitArray(new bool[] { false, false,  true,  true,  true, false, false,  true,  true, false, false, false, false, false,  true,  true, false,  true,  }) },
                { 15, new BitArray(new bool[] { false, false,  true,  true,  true,  true,  true, false, false,  true, false, false,  true, false,  true, false, false, false,  }) },
                { 16, new BitArray(new bool[] { false,  true, false, false, false, false,  true, false,  true,  true, false,  true,  true,  true,  true, false, false, false,  }) },
                { 17, new BitArray(new bool[] { false,  true, false, false, false,  true, false,  true, false, false, false,  true, false,  true,  true,  true, false,  true,  }) },
                { 18, new BitArray(new bool[] { false,  true, false, false,  true, false,  true, false,  true, false, false, false, false,  true, false,  true,  true,  true,  }) },
                { 19, new BitArray(new bool[] { false,  true, false, false,  true,  true, false,  true, false,  true, false, false,  true,  true, false, false,  true, false,  }) },
                { 20, new BitArray(new bool[] { false,  true, false,  true, false, false,  true, false, false,  true,  true, false,  true, false, false,  true,  true, false,  }) },
                { 21, new BitArray(new bool[] { false,  true, false,  true, false,  true, false,  true,  true, false,  true, false, false, false, false, false,  true,  true,  }) },
                { 22, new BitArray(new bool[] { false,  true, false,  true,  true, false,  true, false, false, false,  true,  true, false, false,  true, false, false,  true,  }) },
                { 23, new BitArray(new bool[] { false,  true, false,  true,  true,  true, false,  true,  true,  true,  true,  true,  true, false,  true,  true, false, false,  }) },
                { 24, new BitArray(new bool[] { false,  true,  true, false, false, false,  true,  true,  true, false,  true,  true, false, false, false,  true, false, false,  }) },
                { 25, new BitArray(new bool[] { false,  true,  true, false, false,  true, false, false, false,  true,  true,  true,  true, false, false, false, false,  true,  }) },
                { 26, new BitArray(new bool[] { false,  true,  true, false,  true, false,  true,  true,  true,  true,  true, false,  true, false,  true, false,  true,  true,  }) },
                { 27, new BitArray(new bool[] { false,  true,  true, false,  true,  true, false, false, false, false,  true, false, false, false,  true,  true,  true, false,  }) },
                { 28, new BitArray(new bool[] { false,  true,  true,  true, false, false,  true,  true, false, false, false, false, false,  true,  true, false,  true, false,  }) },
                { 29, new BitArray(new bool[] { false,  true,  true,  true, false,  true, false, false,  true,  true, false, false,  true,  true,  true,  true,  true,  true,  }) },
                { 30, new BitArray(new bool[] { false,  true,  true,  true,  true, false,  true,  true, false,  true, false,  true,  true,  true, false,  true, false,  true,  }) },
                { 31, new BitArray(new bool[] { false,  true,  true,  true,  true,  true, false, false,  true, false, false,  true, false,  true, false, false, false, false,  }) },
                { 32, new BitArray(new bool[] {  true, false, false, false, false, false,  true, false, false,  true,  true,  true, false,  true, false,  true, false,  true,  }) },
                { 33, new BitArray(new bool[] {  true, false, false, false, false,  true, false,  true,  true, false,  true,  true,  true,  true, false, false, false, false,  }) },
                { 34, new BitArray(new bool[] {  true, false, false, false,  true, false,  true, false, false, false,  true, false,  true,  true,  true, false,  true, false,  }) },
                { 35, new BitArray(new bool[] {  true, false, false, false,  true,  true, false,  true,  true,  true,  true, false, false,  true,  true,  true,  true,  true,  }) },
                { 36, new BitArray(new bool[] {  true, false, false,  true, false, false,  true, false,  true,  true, false, false, false, false,  true, false,  true,  true,  }) },
                { 37, new BitArray(new bool[] {  true, false, false,  true, false,  true, false,  true, false, false, false, false,  true, false,  true,  true,  true, false,  }) },
                { 38, new BitArray(new bool[] {  true, false, false,  true,  true, false,  true, false,  true, false, false,  true,  true, false, false,  true, false, false,  }) },
                { 39, new BitArray(new bool[] {  true, false, false,  true,  true,  true, false,  true, false,  true, false,  true, false, false, false, false, false,  true,  }) },
                { 40, new BitArray(new bool[] {  true, false,  true, false, false, false,  true,  true, false, false, false,  true,  true, false,  true, false, false,  true, }) },
            };

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
                var term = new byte[] { 0, (byte)(i-1) };

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
        #endregion

        // (0, 0) - top, left
        // (w, h) - bottom, right 
        private ModuleType[,] modules;
        private int[,] accessCount;
        private bool[,] freeMask;
        private int dim;
        private int fullDim;
        private int qz;
    }
}
