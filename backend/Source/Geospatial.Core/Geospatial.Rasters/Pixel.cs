using System;
using System.Diagnostics;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

[DebuggerDisplay("{Y},{X}")]
public readonly struct Pixel : IEquatable<Pixel>
{
	public static readonly Pixel NullPixel = new Pixel(int.MinValue, int.MinValue);

	public readonly int Row;

	public readonly int Column;

	public static int HashVar1 = 6949;

	public static int HashVar2 = 10009;

	public static int HashVar3 = 10009;

	public int X => Column;

	public int Y => Row;

	public bool IsNull()
	{
		if (Row == int.MinValue)
		{
			return Column == int.MinValue;
		}
		return false;
	}

	public Pixel(int r, int c)
	{
		Row = r;
		Column = c;
	}

	public int To1D(int totalNumColumns)
	{
		return Column + Row * totalNumColumns;
	}

	public Pixel UpperLeft()
	{
		return new Pixel(Row - 1, Column - 1);
	}

	public Pixel Top()
	{
		return new Pixel(Row - 1, Column);
	}

	public Pixel UpperRight()
	{
		return new Pixel(Row - 1, Column + 1);
	}

	public Pixel Left()
	{
		return new Pixel(Row, Column - 1);
	}

	public Pixel Right()
	{
		return new Pixel(Row, Column + 1);
	}

	public Pixel LowerLeft()
	{
		return new Pixel(Row + 1, Column - 1);
	}

	public Pixel Bottom()
	{
		return new Pixel(Row + 1, Column);
	}

	public Pixel LowerRight()
	{
		return new Pixel(Row + 1, Column + 1);
	}

	public Pixel ChildUpperLeft()
	{
		return new Pixel(Row * 2, Column * 2);
	}

	public Pixel ChildUpperRight()
	{
		return new Pixel(Row * 2, Column * 2 + 1);
	}

	public Pixel ChildLowerLeft()
	{
		return new Pixel(Row * 2 + 1, Column * 2);
	}

	public Pixel ChildLowerRight()
	{
		return new Pixel(Row * 2 + 1, Column * 2 + 1);
	}

	public void ChildPixels(out Pixel ul, out Pixel ur, out Pixel ll, out Pixel lr)
	{
		int scaledRow = Row * 2;
		int r = scaledRow + 1;
		int scaledColumn = Column * 2;
		int c = scaledColumn + 1;
		ul = new Pixel(scaledRow, scaledColumn);
		ur = new Pixel(r, scaledColumn);
		ll = new Pixel(scaledRow, c);
		lr = new Pixel(r, c);
	}

	public Point AsPoint()
	{
		return new Point(Row, Column);
	}

	public PixelRange GetRange(int radius)
	{
		return new PixelRange(Y - radius, Y + radius, X - radius, X + radius);
	}

	public override bool Equals(object obj)
	{
		if (obj is Pixel)
		{
			return (Pixel)obj == this;
		}
		return false;
	}

	public bool Equals(Pixel p2)
	{
		if (X == p2.X)
		{
			return Y == p2.Y;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return (HashVar1 * HashVar2 + Column.GetHashCode()) * HashVar3 + Row.GetHashCode();
	}

	public static Pixel operator +(Pixel p1, Pixel p2)
	{
		return new Pixel(p1.Row + p2.Row, p1.Column + p2.Column);
	}

	public static bool operator ==(Pixel p1, Pixel p2)
	{
		if (p1.X == p2.X)
		{
			return p1.Y == p2.Y;
		}
		return false;
	}

	public static bool operator !=(Pixel p1, Pixel p2)
	{
		return !(p1 == p2);
	}

	public override string ToString()
	{
		return $"({Y}, {X})";
	}
}
