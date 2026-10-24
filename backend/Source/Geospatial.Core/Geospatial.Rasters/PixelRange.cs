using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Geospatial.Rasters;

[DebuggerDisplay("X:({Left} to {Right}) Y:({Bottom} to {Top})")]
public readonly struct PixelRange : IEnumerable<Pixel>, IEnumerable
{
	private const int NULLMAX = 2147483646;

	private const int NULLMIN = int.MaxValue;

	public static readonly PixelRange NullRange = new PixelRange(int.MaxValue, 2147483646, int.MaxValue, 2147483646);

	public readonly int MinRow;

	public readonly int MaxRow;

	public readonly int MinCol;

	public readonly int MaxCol;

	public Pixel UpperLeft => new Pixel(MinRow, MinCol);

	public Pixel UpperRight => new Pixel(MinRow, MaxCol);

	public Pixel LowerLeft => new Pixel(MaxRow, MinCol);

	public Pixel LowerRight => new Pixel(MaxRow, MaxCol);

	public int Left => MinCol;

	public int Right => MaxCol;

	public int Top => MinRow;

	public int Bottom => MaxRow;

	public int RowCount => MaxRow - MinRow + 1;

	public int ColCount => MaxCol - MinCol + 1;

	public int CellCount
	{
		get
		{
			long cellCountLong = CellCountLong;
			if (cellCountLong > int.MaxValue)
			{
				return -1;
			}
			return (int)cellCountLong;
		}
	}

	public long CellCountLong => (long)RowCount * (long)ColCount;

	public int Width => ColCount;

	public int Height => RowCount;

	public bool IsNull()
	{
		return IsNull(this);
	}

	public static bool IsNull(PixelRange pr)
	{
		if (pr.MinRow == int.MaxValue && pr.MinCol == int.MaxValue && pr.MaxRow == 2147483646)
		{
			return pr.MaxCol == 2147483646;
		}
		return false;
	}

	public bool InRange(int row, int col)
	{
		if (row >= MinRow && row <= MaxRow && col >= MinCol)
		{
			return col <= MaxCol;
		}
		return false;
	}

	public bool InRange(Pixel px)
	{
		if (px.Row >= MinRow && px.Row <= MaxRow && px.Column >= MinCol)
		{
			return px.Column <= MaxCol;
		}
		return false;
	}

	public bool IsFullRange(RasterDefinition def)
	{
		if (MinRow == 0 && MinCol == 0 && MaxRow == def.Rows - 1)
		{
			return MaxCol == def.Cols - 1;
		}
		return false;
	}

	public bool IsOrExceedsFullRange(RasterDefinition def)
	{
		if (MinRow <= 0 && MinCol <= 0 && MaxRow >= def.Rows - 1)
		{
			return MaxCol >= def.Cols - 1;
		}
		return false;
	}

	public static PixelRange FromRectangle(int left, int top, int width, int height)
	{
		return new PixelRange(top, top + height - 1, left, left + width - 1);
	}

	public PixelRange(int minRow, int maxRow, int minCol, int maxCol)
	{
		MinRow = minRow;
		MaxRow = maxRow;
		MinCol = minCol;
		MaxCol = maxCol;
	}

	public PixelRange(Pixel ul, Pixel lr)
	{
		MinRow = ul.Y;
		MaxRow = lr.Y;
		MinCol = ul.X;
		MaxCol = lr.X;
	}

	public static PixelRange FromUnsortedPixels(Pixel a, Pixel b)
	{
		int minCol = Math.Min(a.X, b.X);
		int maxCol = Math.Max(a.X, b.X);
		int minRow = Math.Min(a.Y, b.Y);
		int maxRow = Math.Max(a.Y, b.Y);
		return new PixelRange(minRow, maxRow, minCol, maxCol);
	}

	public PixelRange Buffer(int numPixels)
	{
		return new PixelRange(MinRow - numPixels, MaxRow + numPixels, MinCol - numPixels, MaxCol + numPixels);
	}

	public bool Overlaps(PixelRange other)
	{
		if (IsNull() || other.IsNull())
		{
			return false;
		}
		if (MinRow > other.MaxRow)
		{
			return false;
		}
		if (MaxRow < other.MinRow)
		{
			return false;
		}
		if (MinCol > other.MaxCol)
		{
			return false;
		}
		if (MaxCol < other.MinCol)
		{
			return false;
		}
		return true;
	}

	public PixelRange ClipTo(RasterDefinition rdef)
	{
		if (Overlaps(rdef.FullRange()))
		{
			int minRow = Math.Max(0, MinRow);
			int minCol = Math.Max(0, MinCol);
			int maxRow = Math.Min(rdef.Rows - 1, MaxRow);
			int maxCol = Math.Min(rdef.Cols - 1, MaxCol);
			return new PixelRange(minRow, maxRow, minCol, maxCol);
		}
		return NullRange;
	}

	public PixelRange Intersection(PixelRange other)
	{
		if (!Overlaps(other))
		{
			return NullRange;
		}
		int minCol = Math.Max(MinCol, other.MinCol);
		int maxCol = Math.Min(MaxCol, other.MaxCol);
		int minRow = Math.Max(MinRow, other.MinRow);
		int maxRow = Math.Min(MaxRow, other.MaxRow);
		return new PixelRange(minRow, maxRow, minCol, maxCol);
	}

	public List<PixelRange> SplitIntoColumns(int divisions)
	{
		List<PixelRange> pixelRanges = new List<PixelRange>(divisions);
		if (divisions == 1)
		{
			pixelRanges.Add(this);
			return pixelRanges;
		}
		int colCountOverDivisions = ColCount / divisions;
		for (int i = 0; i < divisions - 1; i++)
		{
			int minCol = MinCol + i * colCountOverDivisions;
			int maxCol = MinCol + (i + 1) * colCountOverDivisions - 1;
			pixelRanges.Add(new PixelRange(MinRow, MaxRow, minCol, maxCol));
		}
		PixelRange pixelRange = pixelRanges.Last();
		pixelRanges.Add(new PixelRange(MinRow, MaxRow, pixelRange.Right + 1, Right));
		return pixelRanges;
	}

	public List<PixelRange> SplitIntoRows(int divisions)
	{
		List<PixelRange> pixelRanges = new List<PixelRange>(divisions);
		if (divisions == 1)
		{
			pixelRanges.Add(this);
			return pixelRanges;
		}
		int colCountOverDivisions = ColCount / divisions;
		for (int i = 0; i < divisions - 1; i++)
		{
			int minRow = MinRow + i * colCountOverDivisions;
			int maxRow = MinRow + (i + 1) * colCountOverDivisions - 1;
			pixelRanges.Add(new PixelRange(minRow, maxRow, MinCol, MaxCol));
		}
		pixelRanges.Add(new PixelRange(pixelRanges.Last().Bottom + 1, Bottom, MinCol, MaxCol));
		return pixelRanges;
	}

	public PixelRange BufferConstrained(int numPixels, RasterDefinition constraint)
	{
		return new PixelRange(Math.Max(MinRow - numPixels, 0), Math.Min(MaxRow + numPixels, constraint.Rows - 1), Math.Max(MinCol - numPixels, 0), Math.Min(MaxCol + numPixels, constraint.Cols - 1));
	}

	public IEnumerator<Pixel> GetEnumerator()
	{
		return EnumerateInternal();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return EnumerateInternal();
	}

	private IEnumerator<Pixel> EnumerateInternal()
	{
		for (int r = MinRow; r <= MaxRow; r++)
		{
			for (int c = MinCol; c <= MaxCol; c++)
			{
				yield return new Pixel(r, c);
			}
		}
	}

	public override bool Equals(object obj)
	{
		if (!(obj is PixelRange other))
		{
			return false;
		}
		return Equals(other);
	}

	public bool Equals(PixelRange other)
	{
		if (MinRow == other.MinRow && MaxRow == other.MaxRow && MinCol == other.MinCol)
		{
			return MaxCol == other.MaxCol;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return (((unchecked(-1896930968 * -1521134295) + MinRow.GetHashCode()) * -1521134295 + MaxRow.GetHashCode()) * -1521134295 + MinCol.GetHashCode()) * -1521134295 + MaxCol.GetHashCode();
	}

	public static bool operator ==(PixelRange lhs, PixelRange rhs)
	{
		if (lhs.IsNull() && rhs.IsNull())
		{
			return true;
		}
		if (lhs.MinCol == rhs.MinCol && lhs.MaxCol == rhs.MaxCol && lhs.MinRow == rhs.MinRow)
		{
			return lhs.MaxRow == rhs.MaxRow;
		}
		return false;
	}

	public static bool operator !=(PixelRange lhs, PixelRange rhs)
	{
		return !(lhs == rhs);
	}
}
