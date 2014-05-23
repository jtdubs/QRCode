using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace QRCode
{
	public enum ModuleType { Light, Dark }
	public enum CharacterSet { Numeric, AlphaNumeric, Bytes }
	public enum SymbolType { Micro, Normal }

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
		public QRCode(SymbolType type, int version)
		{
			Type = type;
			Version = version;
			Init();
		}

		public SymbolType Type { get; private set; }
		public int Version { get; private set; }

		public void Show()
		{
			for (int y = 0; y < fullDim; y++)
			{
				for (int x = 0; x < fullDim; x++)
				{
					if (modules[x, y] == ModuleType.Dark)
						Console.Write("#");
					else
						Console.Write(" ");
				}
				Console.WriteLine();
			}
		}

		private void Init()
		{
			dim = GetSymbolDimension(Type, Version);
			qz = GetQuietZoneDimension(Type);
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
			DrawFinderPattern(0, 0);

			switch (Type)
			{
			case SymbolType.Micro:
				// draw timing lines
				DrawTimingHLine(8, 0, dim - 8);
				DrawTimingVLine(0, 8, dim - 8);
				break;

			case SymbolType.Normal:
				// draw top-right finder pattern
				DrawFinderPattern(dim - 7, 0);

				// draw bottom-left finder pattern
				DrawFinderPattern(0, dim - 7);

				// draw timing lines
				DrawTimingHLine(8, 6, dim - 8);
				DrawTimingVLine(6, 8, dim - 8 - 8);
				break;
			}
		}

		private void DrawFinderPattern(int left, int top)
		{
			DrawRect(left, top, 7, 7, ModuleType.Dark);
			FillRect(left + 2, top + 2, 3, 3, ModuleType.Dark);
		}

		private void DrawAlignmentPattern(int left, int top)
		{
			DrawRect(left, top, 3, 3, ModuleType.Dark);
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

		// (0, 0) - top, left
		// (w, h) - bottom, right 
		private ModuleType[,] modules;
		private int[,] accessCount;
		private int dim;
		private int fullDim;
		private int qz;

		private static IEnumerable<char> GetValidCharacters(CharacterSet charSet)
		{
			switch (charSet) 
			{
			case CharacterSet.Numeric:
				return new char[] 
				{
					'0', '1', '2', '3', '4', '5', '6', '7', '8', '9' 
				};
			case CharacterSet.AlphaNumeric:
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

		private static int GetSymbolDimension(SymbolType type, int version)
		{
			switch (type)
			{
			case SymbolType.Micro:
				if (version < 1 || version > 4)
					throw new ArgumentException ("version");
				return 9 + (2 * version);
			case SymbolType.Normal:
				if (version < 1 || version > 40)
					throw new ArgumentException ("version");
				return 19 + (2 * version);
			default:
				throw new ArgumentException ("type");
			}
		}

		private static int GetQuietZoneDimension(SymbolType type)
		{
			switch (type)
			{
			case SymbolType.Normal:
				return 4;
			case SymbolType.Micro:
				return 2;
			default:
				throw new ArgumentException("type");
			}
		}
	}

	class MainClass
	{
		public static void Main(string[] args)
		{
			var code = new QRCode(SymbolType.Normal, 3);
			code.Show();

			Console.ReadLine();
		}
	}
}
