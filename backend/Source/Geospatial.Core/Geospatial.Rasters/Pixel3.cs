using System.Diagnostics;

namespace Geospatial.Rasters;

[DebuggerDisplay("{Y},{X}")]
public readonly struct Pixel3
{
	public static readonly Pixel3 NullPixel = new Pixel3(int.MinValue, int.MinValue, int.MinValue);

	public readonly int Row;

	public readonly int Column;

	public readonly int Plane;

	public static int HashVar1 = 6949;

	public static int HashVar2 = 10009;

	public static int HashVar3 = 10009;

	public int X => Column;

	public int Y => Row;

	public int Z => Plane;

	public bool IsNull()
	{
		if (Row == int.MinValue && Column == int.MinValue)
		{
			return Plane == int.MinValue;
		}
		return false;
	}

	public Pixel3(int z, int r, int c)
	{
		Plane = z;
		Row = r;
		Column = c;
	}

	public Pixel3(int z, Pixel px)
	{
		Plane = z;
		Row = px.Row;
		Column = px.Column;
	}

	public int To1D(int totalNumColumns)
	{
		return Column + Row * totalNumColumns;
	}

	public override bool Equals(object obj)
	{
		if (obj is Pixel3)
		{
			return (Pixel3)obj == this;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return ((HashVar1 * HashVar2 + Plane.GetHashCode()) * HashVar2 + Column.GetHashCode()) * HashVar3 + Row.GetHashCode();
	}

	public static Pixel3 operator +(Pixel3 p1, Pixel3 p2)
	{
		return new Pixel3(p1.X + p2.X, p1.Y + p2.Y, p1.Z + p2.Z);
	}

	public static bool operator ==(Pixel3 p1, Pixel3 p2)
	{
		if (p1.X == p2.X && p1.Y == p2.Y)
		{
			return p1.Z == p2.Z;
		}
		return false;
	}

	public static bool operator !=(Pixel3 p1, Pixel3 p2)
	{
		return !(p1 == p2);
	}

	public override string ToString()
	{
		return "(" + Z + ", " + Y + ", " + X + ")";
	}
}
