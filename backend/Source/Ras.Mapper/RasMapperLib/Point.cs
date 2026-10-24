using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RasMapperLib.Utilities;

namespace RasMapperLib;

[DebuggerDisplay("({X}, {Y})")]
public class Point : Feature
{
	protected double _x;

	protected double _y;

	protected double _z;

	protected double _m;

	public double X => _x;

	public double Y => _y;

	public double Z => _z;

	public double M => _m;

	public override string SummaryText
	{
		get
		{
			string textValue = _x + "," + _y;
			if (HasZ)
			{
				textValue = textValue + "," + _z;
			}
			if (HasM)
			{
				textValue = textValue + "," + _m;
			}
			return textValue;
		}
	}

	public override bool HasZ => !double.IsNaN(_z);

	public override bool HasM => !double.IsNaN(_m);

	public override double MaxX => _x;

	public override double MinX => _x;

	public override double MaxY => _y;

	public override double MinY => _y;

	public override double MaxZ => _z;

	public override double MinZ => _z;

	public override double MaxM => _m;

	public override double MinM => _m;

	public override bool MaxMinXYDefined
	{
		get
		{
			if (double.IsNaN(_x))
			{
				return false;
			}
			if (double.IsNaN(_y))
			{
				return false;
			}
			return true;
		}
	}

	public override bool UsesPointSymbol { get; }

	public override bool UsesPen { get; }

	public override bool UsesBrush { get; }

	public override bool UsesSurfaceFill { get; }

	public override int Count { get; }

	public Point(double x, double y)
		: this(x, y, double.NaN, double.NaN)
	{
	}

	public Point(double x, double y, double z)
		: this(x, y, z, double.NaN)
	{
	}

	public Point(Point2Double ptD)
		: this(ptD.X, ptD.Y, double.NaN, double.NaN)
	{
	}

	public Point(Point pt)
		: this(pt.X, pt.Y, pt.Z, pt.M)
	{
	}

	public Point(double x, double y, double z, double m)
	{
		UsesPointSymbol = true;
		UsesPen = false;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 1;
		_x = x;
		_y = y;
		_z = z;
		_m = m;
	}

	public Point(PointM PointM)
	{
		UsesPointSymbol = true;
		UsesPen = false;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 1;
		_x = PointM.X;
		_y = PointM.Y;
		_z = PointM.Z;
		_m = PointM.M;
	}

	public Point(string spaceSeperatedStrings)
	{
		UsesPointSymbol = true;
		UsesPen = false;
		UsesBrush = false;
		UsesSurfaceFill = false;
		Count = 1;
		string[] parts = spaceSeperatedStrings.Split(' ');
		double.TryParse(parts[0], out _x);
		double.TryParse(parts[1], out _y);
		if (parts.Count() > 2)
		{
			double.TryParse(parts[2], out _z);
		}
		else
		{
			_z = double.NaN;
		}
		if (parts.Count() > 3)
		{
			double.TryParse(parts[3], out _m);
		}
		else
		{
			_m = double.NaN;
		}
	}

	public void Stream(BinaryWriter bw)
	{
		bw.Write(X);
		bw.Write(Y);
	}

	public static Point Stream(BinaryReader br)
	{
		return new Point(br.ReadDouble(), br.ReadDouble());
	}

	public double DistanceTo(Point point)
	{
		if (point == null)
		{
			return double.NaN;
		}
		return System.Math.Sqrt(System.Math.Pow(point.X - _x, 2.0) + System.Math.Pow(point.Y - _y, 2.0));
	}

	public double DistanceTo(double x, double y)
	{
		if (double.IsNaN(x))
		{
			return double.NaN;
		}
		return System.Math.Sqrt(System.Math.Pow(x - _x, 2.0) + System.Math.Pow(y - _y, 2.0));
	}

	public override PointM Centroid()
	{
		return PointM();
	}

	public Point SetZ(double z)
	{
		return new Point(_x, _y, z, _m);
	}

	public Point SetM(double m)
	{
		return new Point(_x, _y, _z, m);
	}

	public void SetZTemporary(double zz)
	{
		_z = zz;
	}

	public void SetMTemporary(double mm)
	{
		_m = mm;
	}

	public PointM PointM()
	{
		PointM result = default;
		result.X = _x;
		result.Y = _y;
		result.Z = _z;
		result.M = _m;
		return result;
	}

	public override void MD5HashAppend(Hash hash)
	{
		hash.Add(new double[4] { _x, _y, _z, _m });
	}

	public override string ToString()
	{
		return "{" + System.Math.Round(X, 2) + ", " + System.Math.Round(Y, 2) + ", " + System.Math.Round(Z, 2) + ", " + System.Math.Round(M, 2) + "}";
	}

	public override bool EqualXY(Feature feature)
	{
		if (feature is Point)
		{
			Point point = (Point)feature;
			if (_x == point.X && _y == point.Y)
			{
				return true;
			}
		}
		return false;
	}

	public bool EqualXY(Feature feature, double Tolerance)
	{
		if (feature is Point)
		{
			Point point = (Point)feature;
			if (System.Math.Abs(_x - point.X) <= Tolerance && System.Math.Abs(_y - point.Y) <= Tolerance)
			{
				return true;
			}
		}
		return false;
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		return PointM.CloseTo(_x, _y, nearDistance);
	}

	public override bool Intersects(Extent extent)
	{
		return extent.Contains(this);
	}

	public override PointM LabelPoint(FeatureLayer layer)
	{
		return PointM();
	}

	public string ToCSString()
	{
		return PointM().ToCSString();
	}

	public string ToCSString(string xyFormat, string zmFormat)
	{
		return PointM().ToCSString(xyFormat, zmFormat);
	}

	public string ToTabSString()
	{
		return PointM().ToTabSString();
	}

	public override IEditableFeature GetMutableView()
	{
		return new PointEditable(this);
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return GetAvailablePlotOptions(debugMode);
	}

	public static List<PlotOption> GetAvailablePlotOptions(bool debugMode)
	{
		return new List<PlotOption>();
	}

	public override Feature SetZAll(double newZ)
	{
		return new Point(X, Y, newZ, M);
	}

	public override Feature SetMAll(double newM)
	{
		return new Point(X, Y, Z, newM);
	}

	public override IList<double> GetZPerVertex()
	{
		return new double[1] { Z };
	}

	public override IList<double> GetMPerVertex()
	{
		return new double[1] { M };
	}

	public override Feature SetZPerVertex(IList<double> zs)
	{
		return SetZAll(zs[0]);
	}

	public override Feature SetMPerVertex(IList<double> ms)
	{
		return SetMAll(ms[0]);
	}
}
