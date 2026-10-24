using System;
using System.Collections.Generic;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class Ellipse : AffineFeature
{
	private double _maxX;

	private double _minX;

	private double _maxY;

	private double _minY;

	private EllipseSection _ellipseSection;

	private double _radiusX;

	private double _radiusY;

	public bool IsCircle => _radiusX == _radiusY;

	public double RadiusX => _radiusX;

	public double RadiusY => _radiusY;

	public EllipseSection Section => _ellipseSection;

	public override string SummaryText => "Center: " + Center.ToString() + " RadiusX: " + RadiusX + " RadiusY: " + RadiusY;

	public override double MaxX => _maxX;

	public override double MinX => _minX;

	public override double MaxY => _maxY;

	public override double MinY => _minY;

	public override bool HasZ { get; }

	public override bool HasM { get; }

	public override double MaxZ { get; }

	public override double MinZ { get; }

	public override double MaxM { get; }

	public override double MinM { get; }

	public override int Count { get; }

	public override bool MaxMinXYDefined { get; }

	public override bool UsesPointSymbol { get; }

	public override bool UsesPen { get; }

	public override bool UsesBrush { get; }

	public override bool UsesSurfaceFill { get; }

	public static bool IsValid(Ellipse ellip)
	{
		if (ellip == null)
		{
			return false;
		}
		if (double.IsNaN(ellip.Center.X) || double.IsNaN(ellip.Center.Y) || double.IsNaN(ellip.RadiusX) || double.IsNaN(ellip.RadiusY))
		{
			return false;
		}
		return true;
	}

	public Ellipse(double x, double y, double rad, EllipseSection section = EllipseSection.All)
	{
		HasZ = false;
		HasM = false;
		MaxZ = double.NaN;
		MinZ = double.NaN;
		MaxM = double.NaN;
		MinM = double.NaN;
		Count = 1;
		MaxMinXYDefined = true;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = true;
		UsesSurfaceFill = false;
		Initialize(new PointM(x, y), rad, section);
	}

	public Ellipse(double x, double y, double radX, double radY, double angleRot, EllipseSection section = EllipseSection.All)
	{
		HasZ = false;
		HasM = false;
		MaxZ = double.NaN;
		MinZ = double.NaN;
		MaxM = double.NaN;
		MinM = double.NaN;
		Count = 1;
		MaxMinXYDefined = true;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = true;
		UsesSurfaceFill = false;
		Initialize(new PointM(x, y), radX, radY, angleRot, section);
	}

	public Ellipse(PointM c, double rad, EllipseSection section = EllipseSection.All)
	{
		HasZ = false;
		HasM = false;
		MaxZ = double.NaN;
		MinZ = double.NaN;
		MaxM = double.NaN;
		MinM = double.NaN;
		Count = 1;
		MaxMinXYDefined = true;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = true;
		UsesSurfaceFill = false;
		Initialize(c, rad, section);
	}

	public Ellipse(PointM C, double radX, double radY, double angleRot, EllipseSection section = EllipseSection.All)
	{
		HasZ = false;
		HasM = false;
		MaxZ = double.NaN;
		MinZ = double.NaN;
		MaxM = double.NaN;
		MinM = double.NaN;
		Count = 1;
		MaxMinXYDefined = true;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = true;
		UsesSurfaceFill = false;
		Initialize(C, radX, radY, angleRot, section);
	}

	protected void Initialize(PointM c, double rad, EllipseSection section = EllipseSection.All)
	{
		_center = c;
		_radiusX = rad;
		_radiusY = rad;
		_maxX = c.X + rad;
		_minX = c.X - rad;
		_maxY = c.Y + rad;
		_minY = c.Y - rad;
		_angleRotation = 0.0;
		_ellipseSection = section;
		MakeModificationBoundingBox(Center);
		CanDrawModificationBoundingBox = true;
	}

	protected void Initialize(PointM c, double radX, double radY, double angleRot, EllipseSection section = EllipseSection.All)
	{
		_center = c;
		_radiusX = radX;
		_radiusY = radY;
		_maxX = c.X + radX;
		_minX = c.X - radX;
		_maxY = c.Y + radY;
		_minY = c.Y - radY;
		_angleRotation = angleRot;
		_ellipseSection = section;
		MakeModificationBoundingBox(Center);
		CanDrawModificationBoundingBox = true;
	}

	public override void MD5HashAppend(Hash hash)
	{
		hash.Add(new double[5] { Center.X, Center.Y, RadiusX, RadiusY, AngleRotation });
	}

	public override string ToString()
	{
		return SummaryText;
	}

	public override PointM LabelPoint(FeatureLayer layer)
	{
		return _center;
	}

	public override bool EqualXY(Feature feature)
	{
		if (feature == null)
		{
			return false;
		}
		if (feature is Ellipse)
		{
			Ellipse ellipse = (Ellipse)feature;
			if (ellipse.RadiusX == RadiusX && ellipse.RadiusY == RadiusY && ellipse.Center.EqualXY(Center) && ellipse.AngleRotation == AngleRotation)
			{
				return true;
			}
			return false;
		}
		return false;
	}

	public override Extent GetUnrotatedBoundingBox()
	{
		if (_ellipseSection == EllipseSection.Top)
		{
			return new Extent(_maxX, _minX, _maxY, Center.Y);
		}
		if (_ellipseSection == EllipseSection.Bottom)
		{
			return new Extent(_maxX, _minX, Center.Y, _minY);
		}
		return new Extent(_maxX, _minX, _maxY, _minY);
	}

	public bool ContainsRotatedPt(PointM originalPt, PointM rotatedPt)
	{
		if (_ellipseSection == EllipseSection.Top || _ellipseSection == EllipseSection.Bottom)
		{
			if (_ellipseSection == EllipseSection.Top)
			{
				if (rotatedPt.Y < _center.Y)
				{
					return false;
				}
			}
			else if (_ellipseSection == EllipseSection.Bottom && rotatedPt.Y > _center.Y)
			{
				return false;
			}
		}
		return AllSectionContains(originalPt);
	}

	public override bool Contains(PointM ptm)
	{
		throw new NotImplementedException();
	}

	private bool AllSectionContains(PointM ptm)
	{
		if (IsCircle)
		{
			if (ptm.DistanceTo(Center) <= RadiusX)
			{
				return true;
			}
			return false;
		}
		double systemAngleRotationTerm = System.Math.Cos((0.0 - AngleRotation) * System.Math.PI / 180.0);
		double systemAngleRotationTerm2 = System.Math.Sin((0.0 - AngleRotation) * System.Math.PI / 180.0);
		double radiusXPolynomial = RadiusX * RadiusX;
		double radiusYPolynomial = RadiusY * RadiusY;
		double systemPtmTerm = System.Math.Pow(systemAngleRotationTerm * (ptm.X - Center.X) + systemAngleRotationTerm2 * (ptm.Y - Center.Y), 2.0);
		double systemPtmTerm2 = System.Math.Pow(systemAngleRotationTerm2 * (ptm.X - Center.X) - systemAngleRotationTerm * (ptm.Y - Center.Y), 2.0);
		if (systemPtmTerm / radiusXPolynomial + systemPtmTerm2 / radiusYPolynomial <= 1.0)
		{
			return true;
		}
		return false;
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		throw new NotImplementedException("System.Drawing.Matrix removed");
	}

	private EllipseFormulaVariables GetEllipseFormulaVariables(Point translation = null)
	{
		EllipseFormulaVariables ellipseFormulaVariables = new EllipseFormulaVariables();
		if (translation == null)
		{
			translation = new Point(0.0, 0.0);
		}
		ellipseFormulaVariables.DX = (float)(Center.X - translation.X);
		ellipseFormulaVariables.DY = (float)(Center.Y - translation.Y);
		ellipseFormulaVariables.RX = (float)RadiusX;
		ellipseFormulaVariables.RY = (float)RadiusY;
		ellipseFormulaVariables.A = 1f / ellipseFormulaVariables.RX / ellipseFormulaVariables.RX;
		ellipseFormulaVariables.B = 0f;
		ellipseFormulaVariables.C = 1f / ellipseFormulaVariables.RY / ellipseFormulaVariables.RY;
		ellipseFormulaVariables.D = -2f * ellipseFormulaVariables.DX / ellipseFormulaVariables.RX / ellipseFormulaVariables.RX;
		ellipseFormulaVariables.E = -2f * ellipseFormulaVariables.DY / ellipseFormulaVariables.RY / ellipseFormulaVariables.RY;
		ellipseFormulaVariables.F = ellipseFormulaVariables.DX * ellipseFormulaVariables.DX / ellipseFormulaVariables.RX / ellipseFormulaVariables.RX + ellipseFormulaVariables.DY * ellipseFormulaVariables.DY / ellipseFormulaVariables.RY / ellipseFormulaVariables.RY - 1f;
		return ellipseFormulaVariables;
	}

	private float G(float x, EllipseFormulaVariables eq1, float sign1, EllipseFormulaVariables eq2, float sign2)
	{
		return G1(x, eq1, sign1) - G1(x, eq2, sign2);
	}

	private float G1(float x, EllipseFormulaVariables eq, float root_sign)
	{
		float eqTerm = eq.B * x + eq.E;
		eqTerm *= eqTerm;
		eqTerm -= 4f * eq.C * (eq.A * x * x + eq.D * x + eq.F);
		eqTerm = root_sign * (float)System.Math.Sqrt(eqTerm);
		eqTerm = 0f - (eq.B * x + eq.E) + eqTerm;
		return eqTerm / 2f / eq.C;
	}

	public double PerimeterDistanceToPoint(PointM ptm)
	{
		if (_ellipseSection == EllipseSection.Top || _ellipseSection == EllipseSection.Bottom)
		{
			throw new NotImplementedException("System.Drawing.Matrix removed");
		}
		if (IsCircle)
		{
			return System.Math.Abs(Center.DistanceTo(ptm) - RadiusX);
		}
		throw new NotImplementedException("System.Drawing.Matrix removed");
	}

	public static bool Intersects(PointM centerCircle, double radius, Extent extent)
	{
		return new Ellipse(centerCircle, radius).Intersects(extent);
	}

	public override bool Intersects(Extent extent)
	{
		if (_maxX <= extent.MinX)
		{
			return false;
		}
		if (_minX >= extent.MaxX)
		{
			return false;
		}
		if (_maxY <= extent.MinY)
		{
			return false;
		}
		if (_minY >= extent.MaxY)
		{
			return false;
		}
		if (extent.Contains(Center))
		{
			return true;
		}
		double x = Center.X;
		double y = Center.Y;
		if (x <= extent.MinX)
		{
			if (y >= extent.MaxY)
			{
				return Contains(extent.UpperLeft);
			}
			if (y <= extent.MinY)
			{
				return Contains(extent.LowerLeft);
			}
			return _maxX > extent.MinX;
		}
		if (x >= extent.MaxX)
		{
			if (y >= extent.MaxY)
			{
				return Contains(extent.UpperRight);
			}
			if (y <= extent.MinY)
			{
				return Contains(extent.LowerRight);
			}
			return _minX < extent.MaxX;
		}
		if (y >= extent.MaxY)
		{
			return _minY < extent.MaxY;
		}
		if (y <= extent.MinY)
		{
			return _maxY > extent.MinY;
		}
		return true;
	}

	public override void ScaleFeature(Transform transformer, ModificationBoundingBoxIndices index)
	{
		throw new NotImplementedException("System.Drawing.Matrix removed");
	}

	public override void RotateFeature(Transform transformer)
	{
		if (!IsCircle)
		{
			double angleRotationAngleRotationDegreesTerm = (_angleRotation - ((Affine)transformer).AngleRotationDegrees) % 360.0;
			if (angleRotationAngleRotationDegreesTerm < 0.0)
			{
				angleRotationAngleRotationDegreesTerm += 360.0;
			}
			Initialize(Center, RadiusX, RadiusY, angleRotationAngleRotationDegreesTerm, _ellipseSection);
		}
	}

	public bool IntersectsSegment(SegmentM seg, ref PointM int1, ref double t1, ref PointM int2, ref double t2)
	{
		throw new NotImplementedException("Matrix from System.Drawing removed");
	}

	public override bool Contains(Polyline pline)
	{
		if (pline.PartsCount() != 1)
		{
			throw new NotImplementedException();
		}
		new List<Tuple<double, double>>();
		if (!Polyline.IsValidPolyline(pline))
		{
			return false;
		}
		if (!MBROverlaps(pline))
		{
			return false;
		}
		throw new NotImplementedException("System.Drawing.Matrix removed");
	}

	public override List<Tuple<double, double>> GetContainedSubsegments(Polyline polyline)
	{
		if (polyline.PartsCount() != 1)
		{
			throw new NotImplementedException();
		}
		List<Tuple<double, double>> containedSubsegments = new List<Tuple<double, double>>();
		if (!Polyline.IsValidPolyline(polyline))
		{
			return containedSubsegments;
		}
		double[] cumulativeSegmentLengths = polyline.CumulativeSegmentLengths();
		double cumulativeSegmentLengthSum = 0.0;
		SegmentM segmentM = polyline.Segment(0);
		PointM ptm = segmentM.A;
		double tolerance = 1E-05;
		if (PerimeterDistanceToPoint(ptm) < tolerance)
		{
			ptm = segmentM.PointAtDistance(tolerance);
		}
		bool condition;
		if (Contains(ptm))
		{
			condition = true;
			cumulativeSegmentLengthSum = 0.0;
		}
		else
		{
			condition = false;
		}
		checked
		{
			int count = polyline.SegmentCount() - 1;
			PointM @int = default;
			double t = default;
			PointM int2 = default;
			double t2 = default;
			for (int i = 0; i <= count; i++)
			{
				SegmentM seg = polyline.Segment(i);
				if (!IntersectsSegment(seg, ref @int, ref t, ref int2, ref t2))
				{
					continue;
				}
				if (condition)
				{
					if (t2 > 0.0 && t2 < 1.0)
					{
						double cumulativeSegmentLengthSum2 = seg.Length * t2;
						if (i != 0)
						{
							cumulativeSegmentLengthSum2 += cumulativeSegmentLengths[i - 1];
						}
						containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, cumulativeSegmentLengthSum2));
						condition = false;
					}
					else if (t > 0.0 && t < 1.0)
					{
						double cumulativeSegmentLengthSum3 = seg.Length * t;
						if (i != 0)
						{
							cumulativeSegmentLengthSum3 += cumulativeSegmentLengths[i - 1];
						}
						containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, cumulativeSegmentLengthSum3));
						condition = false;
					}
					continue;
				}
				if (t > 0.0 && t < 1.0)
				{
					double cumulativeSegmentLengthSum4 = seg.Length * t;
					if (i != 0)
					{
						cumulativeSegmentLengthSum4 += cumulativeSegmentLengths[i - 1];
					}
					cumulativeSegmentLengthSum = cumulativeSegmentLengthSum4;
					condition = true;
				}
				if (t2 > 0.0 && t2 < 1.0)
				{
					double cumulativeSegmentLengthSum5 = seg.Length * t2;
					if (i != 0)
					{
						cumulativeSegmentLengthSum5 += cumulativeSegmentLengths[i - 1];
					}
					containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, cumulativeSegmentLengthSum5));
					condition = false;
				}
			}
			if (condition)
			{
				containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, polyline.Length));
			}
			return containedSubsegments;
		}
	}

	public Polygon DiscretizeToPolygon(int degree)
	{
		float scaledPiOverDegree = (float)(System.Math.PI * 2.0 / (double)degree);
		float scaledPiOverDegreeSum = 0f;
		PointMs pointMs = new PointMs();
		checked
		{
			int count = degree - 1;
			for (int i = 0; i <= count; i++)
			{
				scaledPiOverDegreeSum += scaledPiOverDegree;
				pointMs.Add(new PointM(Center.X + RadiusX * System.Math.Cos(scaledPiOverDegreeSum), Center.Y + RadiusY * System.Math.Sin(scaledPiOverDegreeSum)));
			}
			Polygon polygon = new Polygon(pointMs).Rotate(AngleRotation, Center);
			if (Section == EllipseSection.Top || Section == EllipseSection.Bottom)
			{
				polygon = GeoProcessing.IntersectPolygon(polygon, ModificationBoundingBox);
			}
			return polygon;
		}
	}

	public override Polygon DiscretizeToPolygon()
	{
		int degree = System.Math.Max(checked((int)System.Math.Floor(System.Math.Sqrt((RadiusX + RadiusY) / 2.0 * 20.0))), 8);
		return DiscretizeToPolygon(degree);
	}

	public void ComputeProfiles(IList<Polyline> polylines, Profile[] profiles, float zValue, double tolerance = 0.0)
	{
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return new List<PlotOption>();
	}

	public override IEditableFeature GetMutableView()
	{
		return new EllipseEditable(this);
	}

	public override Feature SetZAll(double z)
	{
		return new Ellipse(new PointM(Center.X, Center.Y, z, Center.M), RadiusX, RadiusY, AngleRotation, Section);
	}

	public override Feature SetMAll(double m)
	{
		return new Ellipse(new PointM(Center.X, Center.Y, Center.Z, m), RadiusX, RadiusY, AngleRotation, Section);
	}

	public override IList<double> GetZPerVertex()
	{
		return new double[1] { Center.Z };
	}

	public override IList<double> GetMPerVertex()
	{
		return new double[1] { Center.M };
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
