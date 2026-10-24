using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class Triangle : AffineFeature
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__95_002D0
	{
		public Polyline polyline;

		public _Closure_0024__95_002D0(_Closure_0024__95_002D0 other)
		{
			if (other != null)
			{
				polyline = other.polyline;
			}
		}

		[SpecialName]
		internal double _Lambda_0024__0(PointIntersect pi)
		{
			return polyline.DistanceAlong(pi.BPointOnPolyline);
		}
	}

	private double _maxX;

	private double _minX;

	private double _maxY;

	private double _minY;

	private Point _ptA;

	private Point _ptB;

	private Point _ptC;

	private Point _ptARotated;

	private Point _ptBRotated;

	private Point _ptCRotated;

	private SegmentM _segA;

	private SegmentM _segB;

	private SegmentM _segC;

	private double _width;

	private double _height;

	public double Width => _width;

	public double Height => _height;

	public Point PointA => _ptA;

	public Point PointB => _ptB;

	public Point PointC => _ptC;

	public override string SummaryText => "Point A: " + _ptA.ToString() + " Point B: " + _ptB.ToString() + " Point C: " + _ptC.ToString();

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

	public Triangle(double x, double y, double width)
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
		PointM c = new PointM(x, y);
		Initialize(c, width, width, 0.0);
	}

	public Triangle(double x, double y, double width, double height)
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
		PointM c = new PointM(x, y);
		Initialize(c, width, height, 0.0);
	}

	public Triangle(double x, double y, double width, double height, double angleRotation)
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
		PointM c = new PointM(x, y);
		Initialize(c, width, height, angleRotation);
	}

	public Triangle(PointM center, double width)
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
		Initialize(center, width, width, 0.0);
	}

	public Triangle(PointM center, double width, double height)
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
		Initialize(center, width, height, 0.0);
	}

	public Triangle(PointM center, double width, double height, double angleRotation, bool rotateOnAB = false)
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
		Initialize(center, width, height, angleRotation, rotateOnAB);
	}

	public Triangle(PointM center, Point ptA, Point ptB, Point ptC, double angleRotation)
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
		Initialize(center, ptA, ptB, ptC, angleRotation);
	}

	protected void Initialize(PointM c, double width, double height, double angleRotation, bool rotateOnAB = false)
	{
		_center = c;
		_width = width;
		_height = height;
		_maxX = c.X + width / 2.0;
		_minX = c.X - width / 2.0;
		if (!rotateOnAB)
		{
			_maxY = c.Y + height / 2.0;
			_minY = c.Y - height / 2.0;
		}
		else
		{
			_maxY = c.Y + height;
			_minY = c.Y;
		}
		_angleRotation = angleRotation;
		_ptA = new Point(_minX, _minY);
		_ptB = new Point(_maxX, _minY);
		_ptC = new Point((_minX + _maxX) / 2.0, _maxY);
		_segA = new SegmentM(_ptA.PointM(), _ptB.PointM());
		_segB = new SegmentM(_ptB.PointM(), _ptC.PointM());
		_segC = new SegmentM(_ptC.PointM(), _ptA.PointM());
		Point[] rotatedPoints = GetRotatedPoints();
		_ptARotated = rotatedPoints[0];
		_ptBRotated = rotatedPoints[1];
		_ptCRotated = rotatedPoints[2];
		MakeModificationBoundingBox(Center);
		CanDrawModificationBoundingBox = true;
	}

	protected void Initialize(PointM center, Point ptA, Point ptB, Point ptC, double angleRotation)
	{
		_center = center;
		_ptA = ptA;
		_ptB = ptB;
		_ptC = ptC;
		_segA = new SegmentM(_ptA.PointM(), _ptB.PointM());
		_segB = new SegmentM(_ptB.PointM(), _ptC.PointM());
		_segC = new SegmentM(_ptC.PointM(), _ptA.PointM());
		_angleRotation = angleRotation;
		_minX = System.Math.Min(System.Math.Min(_ptA.X, _ptB.X), _ptC.X);
		_maxX = System.Math.Max(System.Math.Max(_ptA.X, _ptB.X), _ptC.X);
		_minY = System.Math.Min(System.Math.Min(_ptA.Y, _ptB.Y), _ptC.Y);
		_maxY = System.Math.Max(System.Math.Max(_ptA.Y, _ptB.Y), _ptC.Y);
		_width = _maxX - _minX;
		_height = _maxY - _minY;
		Point[] rotatedPoints = GetRotatedPoints();
		_ptARotated = rotatedPoints[0];
		_ptBRotated = rotatedPoints[1];
		_ptCRotated = rotatedPoints[2];
		MakeModificationBoundingBox(center);
		CanDrawModificationBoundingBox = true;
	}

	public override void ScaleFeature(Transform transformer, ModificationBoundingBoxIndices index)
	{
	}

	public override void RotateFeature(Transform transformer)
	{
		double angleRotationAngleRotationDegreesTerm = (_angleRotation - ((Affine)transformer).AngleRotationDegrees) % 360.0;
		if (angleRotationAngleRotationDegreesTerm < 0.0)
		{
			angleRotationAngleRotationDegreesTerm += 360.0;
		}
		Initialize(_center, _ptA, _ptB, _ptC, angleRotationAngleRotationDegreesTerm);
	}

	public override void MD5HashAppend(Hash hash)
	{
		hash.Add(new double[7] { _ptA.X, _ptA.Y, _ptB.X, _ptB.Y, _ptC.X, _ptC.Y, AngleRotation });
	}

	public override Extent GetUnrotatedBoundingBox()
	{
		return GetExtent();
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
		if (feature is Triangle)
		{
			Triangle triangle = (Triangle)feature;
			if (triangle.PointA.EqualXY(PointA) && triangle.PointB.EqualXY(PointB) && triangle.PointC.EqualXY(PointC) && triangle.AngleRotation == AngleRotation)
			{
				return true;
			}
			return false;
		}
		return false;
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		return Contains(PointM);
	}

	public override bool Intersects(Extent extent)
	{
		if (extent.MBROverlaps(this))
		{
			SegmentM segmentM = new SegmentM(_ptARotated.PointM(), _ptBRotated.PointM());
			SegmentM segmentM2 = new SegmentM(_ptBRotated.PointM(), _ptCRotated.PointM());
			SegmentM segmentM3 = new SegmentM(_ptCRotated.PointM(), _ptARotated.PointM());
			if (segmentM.Intersects(extent) || segmentM2.Intersects(extent) || segmentM3.Intersects(extent))
			{
				return true;
			}
			return Contains(extent.UpperLeft);
		}
		return false;
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return new List<PlotOption>();
	}

	public static bool IsValid(Triangle tri)
	{
		if (tri == null)
		{
			return false;
		}
		if (double.IsNaN(tri.Center.X) || double.IsNaN(tri.Center.Y) || double.IsNaN(tri.Width) || double.IsNaN(tri.Height) || double.IsNaN(tri._ptA.X) || double.IsNaN(tri._ptA.Y) || double.IsNaN(tri._ptB.X) || double.IsNaN(tri._ptB.Y) || double.IsNaN(tri._ptC.X) || double.IsNaN(tri._ptC.Y))
		{
			return false;
		}
		return true;
	}

	public override IEditableFeature GetMutableView()
	{
		return new TriangleEditable(this);
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
		checked
		{
			int count = pline.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM ptm = pline.PointM(i);
				if (!Contains(ptm))
				{
					return false;
				}
			}
			return true;
		}
	}

	public override bool Contains(PointM ptm)
	{
		PointM pt = _ptARotated.PointM();
		PointM pt2 = _ptBRotated.PointM();
		PointM pt3 = _ptCRotated.PointM();
		double x = ptm.X;
		double y = ptm.Y;
		double lambda = 0.0;
		double lambda2 = 0.0;
		double lambda3 = 0.0;
		return TriangleShape.Contains(pt, pt2, pt3, x, y, ref lambda, ref lambda2, ref lambda3);
	}

	private Point[] GetRotatedPoints()
	{
		throw new NotImplementedException("System.Drawing.Matrix removed");
	}

	public override List<Tuple<double, double>> GetContainedSubsegments(Polyline polyline)
	{
		_Closure_0024__95_002D0 capturedLocals = default;
		_Closure_0024__95_002D0 capturedLocals2 = new _Closure_0024__95_002D0(capturedLocals);
		capturedLocals2.polyline = polyline;
		if (capturedLocals2.polyline.PartsCount() != 1)
		{
			throw new NotImplementedException();
		}
		List<Tuple<double, double>> containedSubsegments = new List<Tuple<double, double>>();
		if (!Polyline.IsValidPolyline(capturedLocals2.polyline))
		{
			return containedSubsegments;
		}
		List<PointIntersect> lineIntersectionMulti = GetLineIntersectionMulti(capturedLocals2.polyline);
		lineIntersectionMulti = lineIntersectionMulti.OrderBy([SpecialName] (PointIntersect pi) => capturedLocals2.polyline.DistanceAlong(pi.BPointOnPolyline)).ToList();
		bool condition = true;
		SegmentM segmentM = capturedLocals2.polyline.Segment(0);
		PointM pointM = segmentM.A;
		double tolerance = 1E-05;
		if (DistanceToPoint(pointM) < tolerance)
		{
			pointM = segmentM.PointAtDistance(tolerance);
			if (lineIntersectionMulti.Count > 0)
			{
				PointIntersect pointIntersect = lineIntersectionMulti.First();
				if (pointIntersect.BSegmentIndex == 0 && pointIntersect.BFraction * segmentM.Length < tolerance)
				{
					lineIntersectionMulti.RemoveAt(0);
				}
			}
		}
		if (Contains(pointM))
		{
			if (lineIntersectionMulti.Count == 0)
			{
				containedSubsegments.Add(new Tuple<double, double>(0.0, capturedLocals2.polyline.Length));
				return containedSubsegments;
			}
			condition = true;
		}
		else
		{
			if (lineIntersectionMulti.Count == 0)
			{
				return containedSubsegments;
			}
			condition = false;
		}
		double[] cumulativeSegmentLengths = capturedLocals2.polyline.CumulativeSegmentLengths();
		double cumulativeSegmentLengthSum = 0.0;
		checked
		{
			int count = lineIntersectionMulti.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				PointIntersect pointIntersect2 = lineIntersectionMulti[intervalIndex];
				int segmentIndex = pointIntersect2.BPointOnPolyline.SegmentIndex;
				double bFraction = pointIntersect2.BFraction;
				double cumulativeSegmentLengthSum2 = capturedLocals2.polyline.Segment(segmentIndex).Length * bFraction;
				if (segmentIndex != 0)
				{
					cumulativeSegmentLengthSum2 += cumulativeSegmentLengths[segmentIndex - 1];
				}
				if (condition)
				{
					if (cumulativeSegmentLengthSum != cumulativeSegmentLengthSum2)
					{
						containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, cumulativeSegmentLengthSum2));
					}
					condition = false;
				}
				else
				{
					cumulativeSegmentLengthSum = cumulativeSegmentLengthSum2;
					condition = true;
				}
			}
			if (condition && cumulativeSegmentLengthSum != capturedLocals2.polyline.Length)
			{
				containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, capturedLocals2.polyline.Length));
			}
			return containedSubsegments;
		}
	}

	private double CumulativeSegmentLength(int idx)
	{
		double[] doubleValues = new double[3];
		double segASegBTerm = 0.0;
		segASegBTerm = (doubleValues[1] = (doubleValues[0] = segASegBTerm + _segA.Length) + _segB.Length) + _segC.Length;
		doubleValues[2] = segASegBTerm;
		if (idx >= doubleValues.Length)
		{
			return doubleValues[2];
		}
		return doubleValues[idx];
	}

	private List<PointIntersect> GetLineIntersectionMulti(Polyline polyline)
	{
		List<PointIntersect> pointIntersects = new List<PointIntersect>();
		SegmentM segmentM = new SegmentM(_ptARotated.PointM(), _ptBRotated.PointM());
		SegmentM segmentM2 = new SegmentM(_ptBRotated.PointM(), _ptCRotated.PointM());
		SegmentM segmentM3 = new SegmentM(_ptCRotated.PointM(), _ptARotated.PointM());
		List<SegmentM> segmentMs = new SegmentM[3] { segmentM, segmentM2, segmentM3 }.ToList();
		checked
		{
			int count = segmentMs.Count - 1;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM4 = segmentMs[i];
				int count2 = polyline.SegmentCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					if (!segmentM4.Intersects(polyline.Segment(j), ref x, ref y, ref t, ref t2))
					{
						continue;
					}
					if (pointIntersects.Count != 0)
					{
						PointIntersect pointIntersect = pointIntersects.Last();
						if (pointIntersect.Point.X == x && pointIntersect.Point.Y == y)
						{
							continue;
						}
					}
					pointIntersects.Add(new PointIntersect(x, y, i, t, j, t2));
				}
			}
			return pointIntersects;
		}
	}

	private double DistanceToPoint(PointM pt)
	{
		ref SegmentM segA = ref _segA;
		SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
		double d = segA.DistanceToPoint(pt, ref relativePosition);
		ref SegmentM segB = ref _segB;
		relativePosition = SegmentM.RelativePosition.Undefined;
		double d2 = segB.DistanceToPoint(pt, ref relativePosition);
		ref SegmentM segC = ref _segC;
		relativePosition = SegmentM.RelativePosition.Undefined;
		return RasMapperLib.Utilities.Math.Min3(d, d2, segC.DistanceToPoint(pt, ref relativePosition));
	}

	public override Polygon DiscretizeToPolygon()
	{
		PointMs pointMs = new PointMs();
		pointMs.Add(_ptARotated.PointM());
		pointMs.Add(_ptBRotated.PointM());
		pointMs.Add(_ptCRotated.PointM());
		pointMs.Add(_ptARotated.PointM());
		return new Polygon(pointMs);
	}

	public override Feature SetZAll(double z)
	{
		PointM center = new PointM(Center.X, Center.Y, z, Center.M);
		Point ptA = new Point(PointA.X, PointA.Y, z, PointA.M);
		Point ptB = new Point(PointB.X, PointB.Y, z, PointB.M);
		Point ptC = new Point(PointC.X, PointC.Y, z, PointC.M);
		return new Triangle(center, ptA, ptB, ptC, AngleRotation);
	}

	public override Feature SetMAll(double m)
	{
		PointM center = new PointM(Center.X, Center.Y, Center.Z, m);
		Point ptA = new Point(PointA.X, PointA.Y, PointA.Z, m);
		Point ptB = new Point(PointB.X, PointB.Y, PointB.Z, m);
		Point ptC = new Point(PointC.X, PointC.Y, PointC.Z, m);
		return new Triangle(center, ptA, ptB, ptC, AngleRotation);
	}

	public override IList<double> GetZPerVertex()
	{
		return new double[4] { Center.Z, PointA.Z, PointB.Z, PointC.Z };
	}

	public override IList<double> GetMPerVertex()
	{
		return new double[4] { Center.M, PointA.M, PointB.M, PointC.M };
	}

	public override Feature SetZPerVertex(IList<double> zs)
	{
		PointM center = new PointM(Center.X, Center.Y, zs[0], Center.M);
		Point ptA = new Point(PointA.X, PointA.Y, zs[1], PointA.M);
		Point ptB = new Point(PointB.X, PointB.Y, zs[2], PointB.M);
		Point ptC = new Point(PointC.X, PointC.Y, zs[3], PointC.M);
		return new Triangle(center, ptA, ptB, ptC, AngleRotation);
	}

	public override Feature SetMPerVertex(IList<double> ms)
	{
		PointM center = new PointM(Center.X, Center.Y, Center.Z, ms[0]);
		Point ptA = new Point(PointA.X, PointA.Y, PointA.Z, ms[1]);
		Point ptB = new Point(PointB.X, PointB.Y, PointB.Z, ms[2]);
		Point ptC = new Point(PointC.X, PointC.Y, PointC.Z, ms[3]);
		return new Triangle(center, ptA, ptB, ptC, AngleRotation);
	}
}
