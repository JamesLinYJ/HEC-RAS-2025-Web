using System;

namespace Ras.Engine;

public readonly struct CellWaterSourceId(int intValue) : IEquatable<CellWaterSourceId>
{
	public static readonly CellWaterSourceId Invalid = new CellWaterSourceId(-1);

	public int Value { get; } = intValue;

	public bool IsValid => Value >= 0;

	public bool Equals(CellWaterSourceId other)
	{
		return Value == other.Value;
	}

	public override bool Equals(object obj)
	{
		if (obj is CellWaterSourceId other)
		{
			return Equals(other);
		}
		return false;
	}

	public override int GetHashCode()
	{
		return Value;
	}

	public static bool operator ==(CellWaterSourceId left, CellWaterSourceId right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(CellWaterSourceId left, CellWaterSourceId right)
	{
		return !left.Equals(right);
	}

	public override string ToString()
	{
		return Value.ToString();
	}
}
