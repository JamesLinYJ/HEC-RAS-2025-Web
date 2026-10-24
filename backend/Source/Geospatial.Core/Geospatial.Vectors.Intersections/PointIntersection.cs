using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.Intersections;

public struct PointIntersection : IEquatable<PointIntersection>
{
	public class PairCompararer : IEqualityComparer<PointIntersection>
	{
		public bool Equals(PointIntersection x, PointIntersection y)
		{
			if (x.APointIndex != y.APointIndex || x.BPointIndex != y.BPointIndex)
			{
				if (x.APointIndex == y.BPointIndex)
				{
					return x.BPointIndex == y.APointIndex;
				}
				return false;
			}
			return true;
		}

		public int GetHashCode(PointIntersection obj)
		{
			if (obj.APointIndex < obj.BPointIndex)
			{
				return HashCode.Combine(obj.APointIndex, obj.APointIndex);
			}
			return HashCode.Combine(obj.BPointIndex, obj.BPointIndex);
		}
	}

	public int APointIndex;

	public int BPointIndex;

	public bool Equals(PointIntersection other)
	{
		if (APointIndex == other.APointIndex)
		{
			return BPointIndex == other.BPointIndex;
		}
		return false;
	}

	public override bool Equals(object obj)
	{
		if (obj is PointIntersection other)
		{
			return Equals(other);
		}
		return false;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(APointIndex, BPointIndex);
	}
}
