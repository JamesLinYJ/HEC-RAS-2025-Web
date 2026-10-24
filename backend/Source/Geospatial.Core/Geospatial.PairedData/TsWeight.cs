using System.Collections.Generic;

namespace Geospatial.PairedData;

public struct TsWeight
{
	public static readonly TsWeight NullWeight = new TsWeight(-1, 0.0);

	public readonly int Index;

	public readonly double Weight;

	public static bool IsNull(TsWeight tsw)
	{
		return tsw == NullWeight;
	}

	public bool IsNull()
	{
		return IsNull(this);
	}

	public TsWeight(int idx, double w)
	{
		Index = idx;
		Weight = w;
	}

	public double Evaluate(IList<double> values)
	{
		return values[Index] * Weight;
	}

	public float Evaluate(IList<float> values)
	{
		return (float)((double)values[Index] * Weight);
	}

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (obj is TsWeight tsw)
		{
			return Equals(tsw);
		}
		return false;
	}

	public bool Equals(TsWeight tsw)
	{
		if (tsw.Index == Index && tsw.Weight == Weight)
		{
			return true;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return (17 * 23 + Index.GetHashCode()) * 23 + Weight.GetHashCode();
	}

	public static bool operator ==(TsWeight left, TsWeight right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(TsWeight left, TsWeight right)
	{
		return !(left == right);
	}
}
