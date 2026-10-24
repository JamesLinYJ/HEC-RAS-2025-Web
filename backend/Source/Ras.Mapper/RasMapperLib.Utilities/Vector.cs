using System;

namespace RasMapperLib.Utilities;

public abstract class Vector
{
	public static Extent MaximumExtent(Extent extent, Feature feature)
	{
		return MaximumExtent(extent, feature, allowMaxMinValues: false);
	}

	public static Extent MaximumExtent(Extent extent, SegmentM segment)
	{
		if (extent == null)
		{
			return new Extent(segment.A, segment.B);
		}
		if (extent.Contains(segment.A) && extent.Contains(segment.B))
		{
			return extent;
		}
		double maxValue = Math.GetMaxValue(extent.MaxX, segment.MaxX, AllowMaxMinValues: false);
		double minValue = Math.GetMinValue(extent.MinX, segment.MinX, AllowMaxMinValues: false);
		double maxValue2 = Math.GetMaxValue(extent.MaxY, segment.MaxY, AllowMaxMinValues: false);
		double minValue2 = Math.GetMinValue(extent.MinY, segment.MinY, AllowMaxMinValues: false);
		double maxValue3 = Math.GetMaxValue(extent.MaxZ, segment.MaxZ, AllowMaxMinValues: false);
		double minValue3 = Math.GetMinValue(extent.MinZ, segment.MinZ, AllowMaxMinValues: false);
		double maxValue4 = Math.GetMaxValue(extent.MaxM, segment.MaxM, AllowMaxMinValues: false);
		double minValue4 = Math.GetMinValue(extent.MinM, segment.MinM, AllowMaxMinValues: false);
		return new Extent(maxValue, minValue, maxValue2, minValue2, maxValue3, minValue3, maxValue4, minValue4);
	}

	public static Extent MaximumExtent(Extent extent, Feature feature, bool allowMaxMinValues)
	{
		if (feature == null)
		{
			return extent;
		}
		if (extent == null)
		{
			return new Extent(feature);
		}
		if (extent.Contains(feature))
		{
			return extent;
		}
		double maxValue = Math.GetMaxValue(extent.MaxX, feature.MaxX, allowMaxMinValues);
		double minValue = Math.GetMinValue(extent.MinX, feature.MinX, allowMaxMinValues);
		double maxValue2 = Math.GetMaxValue(extent.MaxY, feature.MaxY, allowMaxMinValues);
		double minValue2 = Math.GetMinValue(extent.MinY, feature.MinY, allowMaxMinValues);
		double maxValue3 = Math.GetMaxValue(extent.MaxZ, feature.MaxZ, allowMaxMinValues);
		double minValue3 = Math.GetMinValue(extent.MinZ, feature.MinZ, allowMaxMinValues);
		double maxValue4 = Math.GetMaxValue(extent.MaxM, feature.MaxM, allowMaxMinValues);
		double minValue4 = Math.GetMinValue(extent.MinM, feature.MinM, allowMaxMinValues);
		return new Extent(maxValue, minValue, maxValue2, minValue2, maxValue3, minValue3, maxValue4, minValue4);
	}

	public static double AngleBetween(PointM a, PointM b, PointM c)
	{
		if (b.EqualXY(c))
		{
			return 0.0;
		}
		double computed = b.X - a.X;
		double computed2 = b.Y - a.Y;
		double computed3 = c.X - a.X;
		double computed4 = c.Y - a.Y;
		double computed5 = computed * computed3 + computed2 * computed4;
		double systemTerm = System.Math.Pow(computed * computed + computed2 * computed2, 0.5);
		double systemTerm2 = System.Math.Pow(computed3 * computed3 + computed4 * computed4, 0.5);
		double piSum = System.Math.Acos(computed5 / (systemTerm * systemTerm2));
		if (piSum < 0.0)
		{
			piSum += System.Math.PI;
		}
		return piSum;
	}

	public static double AngleBetween(Point2Double a, Point2Double b, Point2Double c)
	{
		if (System.Math.Abs(b.X - c.X) < 1E-15 && System.Math.Abs(b.Y - c.Y) < 1E-15)
		{
			return 0.0;
		}
		double computed = b.X - a.X;
		double computed2 = b.Y - a.Y;
		double computed3 = c.X - a.X;
		double computed4 = c.Y - a.Y;
		double computed5 = computed * computed3 + computed2 * computed4;
		double systemTerm = System.Math.Pow(computed * computed + computed2 * computed2, 0.5);
		double systemTerm2 = System.Math.Pow(computed3 * computed3 + computed4 * computed4, 0.5);
		double piSum = System.Math.Acos(System.Math.Min(System.Math.Max(computed5 / (systemTerm * systemTerm2), -1.0), 1.0));
		if (piSum < 0.0)
		{
			piSum += System.Math.PI;
		}
		return piSum;
	}

	public static double DotProduct(PointM a, PointM b, PointM c)
	{
		double computed = b.X - a.X;
		double computed2 = b.Y - a.Y;
		double computed3 = c.X - a.X;
		double computed4 = c.Y - a.Y;
		return computed * computed3 + computed2 * computed4;
	}

	public static double CrossProduct(PointM a, PointM b, PointM c)
	{
		double computed = b.X - a.X;
		double computed2 = b.Y - a.Y;
		double computed3 = c.X - a.X;
		double computed4 = c.Y - a.Y;
		return computed * computed4 - computed2 * computed3;
	}

	public static double CrossProduct(Point2Double a, Point2Double b, Point2Double c)
	{
		double computed = b.X - a.X;
		double computed2 = b.Y - a.Y;
		double computed3 = c.X - a.X;
		double computed4 = c.Y - a.Y;
		return computed * computed4 - computed2 * computed3;
	}
}
