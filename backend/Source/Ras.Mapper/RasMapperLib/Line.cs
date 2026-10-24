using System;
using System.Collections.Generic;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class Line : Feature
{
	public readonly double A;

	public readonly double B;

	public readonly double C;

	private readonly double _distanceDenominator;

	public double IsHorizontal => 0 - ((A == 0.0) ? 1 : 0);

	public double IsVertical => 0 - ((B == 0.0) ? 1 : 0);

	public override string SummaryText => ToString();

	public override bool HasZ => false;

	public override bool HasM => false;

	public override double MaxX
	{
		get
		{
			if (B == 0.0)
			{
				return (0.0 - C) / A;
			}
			return double.MaxValue;
		}
	}

	public override double MinX => double.MinValue;

	public override double MaxY
	{
		get
		{
			if (A == 0.0)
			{
				return (0.0 - C) / B;
			}
			return double.MaxValue;
		}
	}

	public override double MinY => double.MinValue;

	public override double MaxZ => double.NaN;

	public override double MinZ => double.NaN;

	public override double MaxM => double.NaN;

	public override double MinM => double.NaN;

	public override bool MaxMinXYDefined => false;

	public override bool UsesPointSymbol { get; }

	public override bool UsesPen { get; }

	public override bool UsesBrush { get; }

	public override bool UsesSurfaceFill { get; }

	public override int Count { get; }

	public Line(double x0, double y0, double x1, double y1)
	{
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 2;
		A = y0 - y1;
		B = x1 - x0;
		C = (0.0 - A) * x0 - B * y0;
		_distanceDenominator = System.Math.Sqrt(System.Math.Pow(A, 2.0) + System.Math.Pow(B, 2.0));
	}

	public Line(Point pointA, Point pointB)
		: this(pointA.X, pointA.Y, pointB.X, pointB.Y)
	{
	}

	public Line(PointM ptA, PointM ptB)
		: this(ptA.X, ptA.Y, ptB.X, ptB.Y)
	{
	}

	public Line(double a, double b, double c)
	{
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 2;
		A = a;
		B = b;
		C = c;
		_distanceDenominator = System.Math.Sqrt(System.Math.Pow(a, 2.0) + System.Math.Pow(b, 2.0));
	}

	public double DistanceToPoint(Point point)
	{
		return DistanceToPoint(point.X, point.Y);
	}

	public double DistanceToPoint(PointM pt)
	{
		return DistanceToPoint(pt.X, pt.Y);
	}

	public double DistanceToPoint(double x, double y)
	{
		return System.Math.Abs(A * x + B * y + C) / _distanceDenominator;
	}

	public double X(double y)
	{
		if (A == 0.0)
		{
			return double.NaN;
		}
		return (0.0 - (B * y + C)) / A;
	}

	public double Y(double x)
	{
		if (B == 0.0)
		{
			return double.NaN;
		}
		return (0.0 - (A * x + C)) / B;
	}

	public bool CreateLineSegment(RasterM raster, ref SegmentM segment)
	{
		return CreateLineSegment(raster.MinX, raster.MaxX, raster.MinY, raster.MaxY, ref segment);
	}

	public bool CreateLineSegment(Extent extent, ref SegmentM segment)
	{
		return CreateLineSegment(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY, ref segment);
	}

	public bool CreateLineSegment(double rMinX, double rMaxX, double rMinY, double rMaxY, ref SegmentM segment)
	{
		if (IsHorizontal != 0.0)
		{
			if (RasMapperLib.Utilities.Math.IsInRange(MaxX, rMinY, rMaxY))
			{
				segment.A.X = rMinX;
				segment.A.Y = MaxX;
				segment.B.X = rMaxX;
				segment.B.Y = MaxX;
				return true;
			}
			return false;
		}
		if (IsVertical != 0.0)
		{
			if (RasMapperLib.Utilities.Math.IsInRange(MaxY, rMinX, rMaxX))
			{
				segment.A.X = MaxX;
				segment.A.Y = rMinY;
				segment.B.X = MaxX;
				segment.B.Y = rMaxY;
				return true;
			}
			return false;
		}
		segment.A.X = rMinX;
		segment.A.Y = Y(rMinX);
		segment.B.X = rMaxX;
		segment.B.Y = Y(rMaxX);
		if (segment.A.Y > rMaxY && segment.B.Y > rMaxY)
		{
			return false;
		}
		if (segment.A.Y < rMinY && segment.B.Y < rMinY)
		{
			return false;
		}
		if (segment.A.Y < rMinY)
		{
			segment.A.X = X(rMinY);
			segment.A.Y = rMinY;
		}
		else if (segment.A.Y > rMaxY)
		{
			segment.A.X = X(rMaxY);
			segment.A.Y = rMaxY;
		}
		if (segment.B.Y < rMinY)
		{
			segment.B.X = X(rMinY);
			segment.B.Y = rMinY;
		}
		else if (segment.B.Y > rMaxY)
		{
			segment.B.X = X(rMaxY);
			segment.B.Y = rMaxY;
		}
		return true;
	}

	public override string ToString()
	{
		return "Line";
	}

	public override bool EqualXY(Feature feature)
	{
		if (feature is Line)
		{
			Line line = (Line)feature;
			return A == line.A && B == line.B && C == line.C;
		}
		return false;
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		return DistanceToPoint(PointM) < nearDistance;
	}

	public override bool Intersects(Extent extent)
	{
		SegmentM segment = default;
		return CreateLineSegment(extent, ref segment);
	}

	public override PointM LabelPoint(FeatureLayer layer)
	{
		throw new NotImplementedException();
	}

	public override void MD5HashAppend(Hash hash)
	{
		hash.Add(new double[4] { A, B, C, _distanceDenominator });
	}

	public override IEditableFeature GetMutableView()
	{
		throw new NotImplementedException();
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return new List<PlotOption>();
	}

	public override Feature SetZAll(double z)
	{
		throw new NotImplementedException();
	}

	public override Feature SetMAll(double m)
	{
		throw new NotImplementedException();
	}

	public override IList<double> GetZPerVertex()
	{
		throw new NotImplementedException();
	}

	public override IList<double> GetMPerVertex()
	{
		throw new NotImplementedException();
	}

	public override Feature SetZPerVertex(IList<double> zs)
	{
		throw new NotImplementedException();
	}

	public override Feature SetMPerVertex(IList<double> ms)
	{
		throw new NotImplementedException();
	}
}
