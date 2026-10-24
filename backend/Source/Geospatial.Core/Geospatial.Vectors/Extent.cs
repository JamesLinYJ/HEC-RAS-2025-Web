using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.Transformers;

namespace Geospatial.Vectors;

[DebuggerDisplay("X:({Left} to {Right}) Y:({Bot} to {Top})")]
public readonly struct Extent : IExtent, IEquatable<Extent>
{
	public enum State
	{
		Invalid,
		Finite,
		Infinite
	}

	public static readonly Extent Infinite = new Extent(double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity);

	private const double EffectiveMaxDouble = 1E+32;

	public static readonly Extent EffectivelyInfinite = new Extent(-1E+32, 1E+32, -1E+32, 1E+32);

	public static readonly Extent NullExtent = new Extent(double.NaN, double.NaN, double.NaN, double.NaN);

	public readonly double MinX;

	public readonly double MaxX;

	public readonly double MinY;

	public readonly double MaxY;

	public const int DefaultBorderPointCount = 8;

	public bool IsZeroWidth => MinX == MaxX;

	public bool IsZeroHeight => MinY == MaxY;

	public bool IsDegenerate
	{
		get
		{
			if (!IsZeroWidth)
			{
				return IsZeroHeight;
			}
			return true;
		}
	}

	public bool IsDegenerateLine
	{
		get
		{
			if (IsDegenerate)
			{
				if (IsZeroWidth)
				{
					return !IsZeroHeight;
				}
				return true;
			}
			return false;
		}
	}

	public bool IsDegeneratePoint
	{
		get
		{
			if (IsZeroWidth)
			{
				return IsZeroHeight;
			}
			return false;
		}
	}

	public State Boundedness
	{
		get
		{
			if (IsValid())
			{
				if (!IsFiniteAndValid())
				{
					return State.Infinite;
				}
				return State.Finite;
			}
			return State.Invalid;
		}
	}

	public double Width => DeltaX;

	public double DeltaX
	{
		get
		{
			if (double.IsNaN(MaxX) || double.IsNaN(MinX))
			{
				return 0.0;
			}
			return MaxX - MinX;
		}
	}

	public double Height => DeltaY;

	public double DeltaY
	{
		get
		{
			if (double.IsNaN(MaxY) || double.IsNaN(MinY))
			{
				return 0.0;
			}
			return MaxY - MinY;
		}
	}

	public double Area => Width * Height;

	public double DeltaXY => Math.Sqrt(DeltaX * DeltaX + DeltaY * DeltaY);

	public Point UpperLeft => new Point(MinX, MaxY);

	public Point UpperRight => new Point(MaxX, MaxY);

	public Point LowerLeft => new Point(MinX, MinY);

	public Point LowerRight => new Point(MaxX, MinY);

	public Point Center => new Point((MaxX + MinX) * 0.5, (MaxY + MinY) * 0.5);

	public Point LeftMidside => new Point(MinX, (MaxY + MinY) * 0.5);

	public Point RightMidside => new Point(MaxX, (MaxY + MinY) * 0.5);

	public Point UpperMidside => new Point((MaxX + MinX) * 0.5, MaxY);

	public Point LowerMidside => new Point((MaxX + MinX) * 0.5, MinY);

	public Segment TopWall => new Segment(UpperLeft, UpperRight);

	public Segment BottomWall => new Segment(LowerRight, LowerLeft);

	public Segment LeftWall => new Segment(LowerLeft, UpperLeft);

	public Segment RightWall => new Segment(UpperRight, LowerRight);

	public double Top => MaxY;

	public double Bot => MinY;

	public double Bottom => MinY;

	public double Left => MinX;

	public double Right => MaxX;

	Extent IExtent.Extent => this;

	public double RoughlyEpsilon => MaxOordinate / 100000000000000.0;

	public double MaxDimension => Math.Max(Width, Height);

	public double MaxOordinate => Math.Max(Math.Max(Math.Max(Math.Abs(Left), Math.Abs(Right)), Math.Abs(Top)), Math.Abs(Bottom));

	public bool IsNull()
	{
		if (!double.IsNaN(MinX) && !double.IsNaN(MinY) && !double.IsNaN(MaxX))
		{
			return double.IsNaN(MaxY);
		}
		return true;
	}

	public bool IsValid()
	{
		if (!IsNull() && MinX < MaxX)
		{
			return MinY < MaxY;
		}
		return false;
	}

	public bool IsFiniteAndValid()
	{
		if (IsValid() && MinX > -1E+32 && MaxX < 1E+32 && MinY > -1E+32)
		{
			return MaxY < 1E+32;
		}
		return false;
	}

	public bool IsValidAndFinite()
	{
		if (IsValid())
		{
			return IsFiniteAndValid();
		}
		return false;
	}

	public bool IsFinite()
	{
		if (!IsNull() && MinX > -1E+32 && MaxX < 1E+32 && MinY > -1E+32)
		{
			return MaxY < 1E+32;
		}
		return false;
	}

	public bool IsInfiniteOrInvalid()
	{
		return !IsFiniteAndValid();
	}

	public bool IsInfiniteAndValid()
	{
		if (!IsFiniteAndValid())
		{
			return IsValid();
		}
		return false;
	}

	public Extent(double minX, double maxX, double minY, double maxY)
	{
		MinX = minX;
		MaxX = maxX;
		MinY = minY;
		MaxY = maxY;
	}

	public Extent(Point upperLeft, double width, double height)
		: this(upperLeft.X, upperLeft.X + width, upperLeft.Y - height, upperLeft.Y)
	{
	}

	public Extent(Point upperLeft, Point lowerRight)
		: this(upperLeft.X, lowerRight.X, lowerRight.Y, upperLeft.Y)
	{
	}

	public static Extent FromUnsortedPoints(Point a, Point b)
	{
		double minX = Math.Min(a.X, b.X);
		double maxX = Math.Max(a.X, b.X);
		double minY = Math.Min(a.Y, b.Y);
		double maxY = Math.Max(a.Y, b.Y);
		return new Extent(minX, maxX, minY, maxY);
	}

	public static Extent UnitExtent()
	{
		return new Extent(0.0, 1.0, 0.0, 1.0);
	}

	public static Extent Union(IEnumerable<Extent> extents)
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		foreach (Extent extent in extents)
		{
			if (!extent.IsNull())
			{
				maxValue = Math.Min(maxValue, extent.MinX);
				minValue = Math.Max(minValue, extent.MaxX);
				maxValue2 = Math.Min(maxValue2, extent.MinY);
				minValue2 = Math.Max(minValue2, extent.MaxY);
			}
		}
		if (maxValue > minValue || maxValue2 > minValue2)
		{
			return NullExtent;
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	public static Extent Union<T>(IEnumerable<T> iexts) where T : IExtent
	{
		return Union(iexts.Select((T ie) => ie?.Extent ?? NullExtent).ToArray());
	}

	public static Extent Union(scoped ReadOnlySpan<Extent> extents)
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		ReadOnlySpan<Extent> extentsSpan = extents;
		for (int i = 0; i < extentsSpan.Length; i++)
		{
			Extent extent = extentsSpan[i];
			if (!extent.IsNull())
			{
				maxValue = Math.Min(maxValue, extent.MinX);
				minValue = Math.Max(minValue, extent.MaxX);
				maxValue2 = Math.Min(maxValue2, extent.MinY);
				minValue2 = Math.Max(minValue2, extent.MaxY);
			}
		}
		if (maxValue > minValue || maxValue2 > minValue2)
		{
			return NullExtent;
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	public static Extent Union(params Extent[] extents)
	{
		return Union((IEnumerable<Extent>)extents);
	}

	public static Extent Union(Extent a, Extent b)
	{
		_003C_003Ey__InlineArray2<Extent> buffer = default;
		buffer[0] = a;
		buffer[1] = b;
		return Union(buffer);
	}

	public static Extent Union(params IExtent[] extents)
	{
		return Union(extents.Select((IExtent ie) => ie.Extent));
	}

	public static Extent Union(ReadOnlySpan<Point> points)
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		for (int i = 0; i < points.Length; i++)
		{
			Point point = points[i];
			maxValue = Math.Min(maxValue, point.X);
			minValue = Math.Max(minValue, point.X);
			maxValue2 = Math.Min(maxValue2, point.Y);
			minValue2 = Math.Max(minValue2, point.Y);
		}
		if (maxValue > minValue || maxValue2 > minValue2)
		{
			return NullExtent;
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	public static Extent Union(IEnumerable<Point> points)
	{
		return Union(points.Select((Point p) => new Extent(p, 0.0, 0.0)).ToArray());
	}

	public static Extent Union(IReadOnlyList<Segment> segs)
	{
		if (segs == null || segs.Count == 0)
		{
			return NullExtent;
		}
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		for (int i = 0; i < segs.Count; i++)
		{
			Segment segment = segs[i];
			if (segment.A.X < maxValue)
			{
				maxValue = segment.A.X;
			}
			if (segment.A.X > minValue)
			{
				minValue = segment.A.X;
			}
			if (segment.B.X < maxValue)
			{
				maxValue = segment.B.X;
			}
			if (segment.B.X > minValue)
			{
				minValue = segment.B.X;
			}
			if (segment.A.Y < maxValue2)
			{
				maxValue2 = segment.A.Y;
			}
			if (segment.A.Y > minValue2)
			{
				minValue2 = segment.A.Y;
			}
			if (segment.B.Y < maxValue2)
			{
				maxValue2 = segment.B.Y;
			}
			if (segment.B.Y > minValue2)
			{
				minValue2 = segment.B.Y;
			}
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	public static Extent CenteredOn(Point pt, double width, double height)
	{
		return new Extent(pt.Translate((0.0 - width) / 2.0, height / 2.0), width, height);
	}

	public static Extent CenteredOn(Point pt, double width)
	{
		return CenteredOn(pt, width, width);
	}

	public bool Contains(Extent other)
	{
		if (other.MinX <= MinX)
		{
			return false;
		}
		if (other.MaxX >= MaxX)
		{
			return false;
		}
		if (other.MinY <= MinY)
		{
			return false;
		}
		if (other.MaxY >= MaxY)
		{
			return false;
		}
		return true;
	}

	public bool ContainsOrEquals(Extent other)
	{
		if (other.MinX < MinX)
		{
			return false;
		}
		if (other.MaxX > MaxX)
		{
			return false;
		}
		if (other.MinY < MinY)
		{
			return false;
		}
		if (other.MaxY > MaxY)
		{
			return false;
		}
		return true;
	}

	public bool Contains(Point pt)
	{
		return Contains(pt.X, pt.Y);
	}

	public bool Contains(double x, double y)
	{
		if (x >= MinX && x < MaxX && y > MinY)
		{
			return y <= MaxY;
		}
		return false;
	}

	public bool ContainsRoughly(Point pt)
	{
		return ContainsRoughly(pt.X, pt.Y, RoughlyEpsilon);
	}

	public bool ContainsRoughly(Point pt, double tol)
	{
		return ContainsRoughly(pt.X, pt.Y, tol);
	}

	public bool ContainsRoughly(double x, double y, double tol)
	{
		if (x >= MinX - tol && x < MaxX + tol && y > MinY - tol)
		{
			return y < MaxY + tol;
		}
		return false;
	}

	public bool ContainsOrTouches(Point pt)
	{
		if (pt.X >= MinX && pt.X <= MaxX && pt.Y >= MinY)
		{
			return pt.Y <= MaxY;
		}
		return false;
	}

	public bool ContainsConfidently(double x, double y)
	{
		if (x >= MinX + RoughlyEpsilon && x < MaxX - RoughlyEpsilon && y > MinY + RoughlyEpsilon)
		{
			return y <= MaxY - RoughlyEpsilon;
		}
		return false;
	}

	public bool Overlaps(in Extent other)
	{
		return Overlaps(other.MinX, other.MaxX, other.MinY, other.MaxY);
	}

	public bool Overlaps(in Segment other)
	{
		return Overlaps(other.MinX, other.MaxX, other.MinY, other.MaxY);
	}

	public bool MBROverlaps(IExtent other)
	{
		return Overlaps(other.Extent);
	}

	public bool Overlaps(double minX, double maxX, double minY, double maxY)
	{
		if (minX > MaxX)
		{
			return false;
		}
		if (maxX < MinX)
		{
			return false;
		}
		if (minY > MaxY)
		{
			return false;
		}
		if (maxY < MinY)
		{
			return false;
		}
		return true;
	}

	public bool Overlaps_EdgePedantic(double minX, double maxX, double minY, double maxY)
	{
		if (minX >= MaxX)
		{
			return false;
		}
		if (maxX < MinX)
		{
			return false;
		}
		if (minY > MaxY)
		{
			return false;
		}
		if (maxY <= MinY)
		{
			return false;
		}
		return true;
	}

	public bool Overlaps_EdgePedantic(Extent other)
	{
		return Overlaps_EdgePedantic(other.MinX, other.MaxX, other.MinY, other.MaxY);
	}

	public bool StrictlyOverlaps(double minX, double maxX, double minY, double maxY)
	{
		if (minX >= MaxX)
		{
			return false;
		}
		if (maxX <= MinX)
		{
			return false;
		}
		if (minY >= MaxY)
		{
			return false;
		}
		if (maxY <= MinY)
		{
			return false;
		}
		return true;
	}

	public Point NearestPoint(Point pt, out double actualDistance)
	{
		Point point = LeftWall.NearestPoint(pt);
		double distanceTo = pt.DistanceTo(point);
		Point result = point;
		double actualDistance2 = distanceTo;
		point = TopWall.NearestPoint(pt);
		distanceTo = pt.DistanceTo(point);
		if (distanceTo < actualDistance2)
		{
			actualDistance2 = distanceTo;
			result = point;
		}
		point = RightWall.NearestPoint(pt);
		distanceTo = pt.DistanceTo(point);
		if (distanceTo < actualDistance2)
		{
			actualDistance2 = distanceTo;
			result = point;
		}
		point = BottomWall.NearestPoint(pt);
		distanceTo = pt.DistanceTo(point);
		if (distanceTo < actualDistance2)
		{
			actualDistance2 = distanceTo;
			result = point;
		}
		actualDistance = actualDistance2;
		return result;
	}

	public Extent Translate(Vector v)
	{
		return Translate(v.DeltaX, v.DeltaY);
	}

	public Extent Translate(double xOffset, double yOffset)
	{
		return new Extent(MinX + xOffset, MaxX + xOffset, MinY + yOffset, MaxY + yOffset);
	}

	public Extent MoveLeft(double dist)
	{
		return Translate(0.0 - dist, 0.0);
	}

	public Extent MoveRight(double dist)
	{
		return Translate(dist, 0.0);
	}

	public Extent MoveUp(double dist)
	{
		return Translate(0.0, dist);
	}

	public Extent MoveDown(double dist)
	{
		return Translate(0.0, 0.0 - dist);
	}

	public Extent CenterOn(Point pt)
	{
		return Translate(Center.To(pt));
	}

	public Extent Extend(double delta)
	{
		return new Extent(MinX - delta, MaxX + delta, MinY - delta, MaxY + delta);
	}

	public Extent Extend(double dx, double dy)
	{
		return new Extent(MinX - dx, MaxX + dx, MinY - dy, MaxY + dy);
	}

	public Extent ExtendLeft(double dist)
	{
		return new Extent(MinX - dist, MaxX, MinY, MaxY);
	}

	public Extent ExtendRight(double dist)
	{
		return new Extent(MinX, MaxX + dist, MinY, MaxY);
	}

	public Extent ExtendUp(double dist)
	{
		return new Extent(MinX, MaxX, MinY, MaxY + dist);
	}

	public Extent ExtendDown(double dist)
	{
		return new Extent(MinX, MaxX, MinY - dist, MaxY);
	}

	public Extent Ratio(double ratio, Point origin)
	{
		if (!IsValid())
		{
			return NullExtent;
		}
		double maxX = origin.X + (MaxX - origin.X) * ratio;
		double minX = origin.X - (origin.X - MinX) * ratio;
		double maxY = origin.Y + (MaxY - origin.Y) * ratio;
		double minY = origin.Y - (origin.Y - MinY) * ratio;
		return new Extent(minX, maxX, minY, maxY);
	}

	public Extent Ratio(double ratioX, double ratioY, Point origin)
	{
		if (!IsValid())
		{
			return NullExtent;
		}
		double maxX = origin.X + (MaxX - origin.X) * ratioX;
		double minX = origin.X - (origin.X - MinX) * ratioX;
		double maxY = origin.Y + (MaxY - origin.Y) * ratioY;
		double minY = origin.Y - (origin.Y - MinY) * ratioY;
		return new Extent(minX, maxX, minY, maxY);
	}

	public Extent RatioAroundCenter(double ratio)
	{
		if (!IsValid())
		{
			return NullExtent;
		}
		Point center = Center;
		double deltaXTimesRatio = DeltaX * ratio;
		double maxX = center.X + deltaXTimesRatio * 0.5;
		double minX = center.X - deltaXTimesRatio * 0.5;
		double deltaYTimesRatio = DeltaY * ratio;
		double maxY = center.Y + deltaYTimesRatio * 0.5;
		double minY = center.Y - deltaYTimesRatio * 0.5;
		return new Extent(minX, maxX, minY, maxY);
	}

	public Extent EnforceAspectRatio(double width, double height)
	{
		double heightOverWidth = height / width;
		double deltaX = DeltaX;
		double deltaY = DeltaY;
		double maxX;
		double minX;
		double maxY;
		double minY;
		if (deltaX != 0.0)
		{
			double deltaYOverDeltaX = deltaY / deltaX;
			Point center = Center;
			if (heightOverWidth < deltaYOverDeltaX)
			{
				double deltaYOverHeightOverWidth = deltaY / heightOverWidth;
				maxX = center.X + deltaYOverHeightOverWidth * 0.5;
				minX = center.X - deltaYOverHeightOverWidth * 0.5;
				maxY = MaxY;
				minY = MinY;
			}
			else
			{
				maxX = MaxX;
				minX = MinX;
				double deltaXTerm = deltaX * heightOverWidth;
				maxY = center.Y + deltaXTerm * 0.5;
				minY = center.Y - deltaXTerm * 0.5;
			}
		}
		else
		{
			double deltaYOverHeightOverWidth2 = deltaY / heightOverWidth;
			maxX = MinX + deltaYOverHeightOverWidth2 * 0.5;
			minX = MinX - deltaYOverHeightOverWidth2 * 0.5;
			maxY = MaxY;
			minY = MinY;
		}
		return new Extent(minX, maxX, minY, maxY);
	}

	public Extent Union(Extent other)
	{
		if (IsNull())
		{
			return other;
		}
		if (other.IsNull())
		{
			return this;
		}
		return new Extent(Math.Min(MinX, other.MinX), Math.Max(MaxX, other.MaxX), Math.Min(MinY, other.MinY), Math.Max(MaxY, other.MaxY));
	}

	public Extent Union(Extent? other)
	{
		if (!other.HasValue)
		{
			return this;
		}
		return Union(other.Value);
	}

	public Extent Update(Point point)
	{
		double maxY = Math.Max(point.Y, Top);
		double minY = Math.Min(point.Y, Bot);
		double minX = Math.Min(point.X, Left);
		double maxX = Math.Max(point.X, Right);
		return new Extent(minX, maxX, minY, maxY);
	}

	public Extent Intersection(Extent other)
	{
		if (IsNull() || other.IsNull() || !Overlaps(in other))
		{
			return NullExtent;
		}
		return new Extent(Math.Max(MinX, other.MinX), Math.Min(MaxX, other.MaxX), Math.Max(MinY, other.MinY), Math.Min(MaxY, other.MaxY));
	}

	public Extent EnforceMinDimensions(double width, double height)
	{
		if (width < 0.0 || height < 0.0)
		{
			throw new ArgumentException();
		}
		Point center = Center;
		double minX = Left;
		double maxX = Right;
		double maxY = Top;
		double minY = Bottom;
		if (width > 0.0 && DeltaX < width)
		{
			minX = center.X - width / 2.0;
			maxX = center.X + width / 2.0;
		}
		if (height > 0.0 && DeltaY < height)
		{
			maxY = center.Y + height / 2.0;
			minY = center.Y - height / 2.0;
		}
		return new Extent(minX, maxX, minY, maxY);
	}

	public Extent EnforceMinWidth(double width)
	{
		return EnforceMinDimensions(width, 0.0);
	}

	public Extent EnforceMinHeight(double height)
	{
		return EnforceMinDimensions(0.0, height);
	}

	public Extent EnforceMinDXDYRatio(double ratio)
	{
		if (DeltaX > DeltaY)
		{
			double deltaXTimesRatio = DeltaX * ratio;
			if (DeltaY < deltaXTimesRatio)
			{
				return EnforceMinHeight(deltaXTimesRatio);
			}
		}
		else
		{
			double deltaYTimesRatio = DeltaY * ratio;
			if (DeltaX < deltaYTimesRatio)
			{
				return EnforceMinWidth(deltaYTimesRatio);
			}
		}
		return Clone();
	}

	public Extent Transform(ISpatialTransformer transformer)
	{
		Point[] edgeDivisorPoints = GetEdgeDivisorPoints(6);
		Point[] points = new Point[edgeDivisorPoints.Length];
		transformer.Transform(edgeDivisorPoints, points);
		return PointCollection.ComputeExtent(points);
	}

	public Extent TransformRasterized(ISpatialTransformer transformer)
	{
		Point[] edgeDivisorPoints = GetEdgeDivisorPoints(6);
		Point[] points = new Point[edgeDivisorPoints.Length];
		transformer.Transform(edgeDivisorPoints, points);
		return PointCollection.ComputeExtent(points);
	}

	public Extent Transform(ISpatialTransformer transformer, out Point[] transformedEdgeDivisorPoints)
	{
		Point[] edgeDivisorPoints = GetEdgeDivisorPoints(6);
		transformedEdgeDivisorPoints = new Point[edgeDivisorPoints.Length];
		transformer.Transform(edgeDivisorPoints, transformedEdgeDivisorPoints);
		return PointCollection.ComputeExtent(transformedEdgeDivisorPoints);
	}

	public static Extent? TryParseExtent(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return null;
		}
		if (!s.Contains(','))
		{
			return null;
		}
		if (s.StartsWith("("))
		{
			s = s.Substring(1);
		}
		if (s.EndsWith(")"))
		{
			s = s.Substring(0, s.Length - 1);
		}
		string[] parts = s.Split(',');
		if (parts.Length != 4)
		{
			return null;
		}
		string s2 = parts[0];
		string s3 = parts[1];
		string s4 = parts[2];
		string s5 = parts[3];
		if (double.TryParse(s2, out var result) && double.TryParse(s3, out var result2) && double.TryParse(s4, out var result3) && double.TryParse(s5, out var result4))
		{
			return new Extent(result, result2, result3, result4);
		}
		return null;
	}

	public Extent Clone()
	{
		return new Extent(MinX, MaxX, MinY, MaxY);
	}

	public static bool operator ==(Extent e1, Extent e2)
	{
		if (e1.MinX == e2.MinX && e1.MaxX == e2.MaxX && e1.MinY == e2.MinY && e1.MaxY == e2.MaxY)
		{
			return true;
		}
		return false;
	}

	public static bool operator !=(Extent e1, Extent e2)
	{
		return !(e1 == e2);
	}

	public bool ToleranceEquals(Extent other, double tol = 0.001)
	{
		double absoluteLeftDifference = Math.Abs(other.Left - Left);
		double absoluteRightDifference = Math.Abs(other.Right - Right);
		double absoluteTopDifference = Math.Abs(other.Top - Top);
		double absoluteBotDifference = Math.Abs(other.Bot - Bot);
		if (absoluteLeftDifference <= tol && absoluteRightDifference <= tol && absoluteTopDifference <= tol)
		{
			return absoluteBotDifference <= tol;
		}
		return false;
	}

	public bool EqualsXY(Extent other)
	{
		if (other.Left == Left && other.Right == Right && other.Top == Top)
		{
			return other.Bot == Bot;
		}
		return false;
	}

	public bool EqualsXYOrBothNull(Extent other)
	{
		if (!IsNull() || !other.IsNull())
		{
			return EqualsXY(other);
		}
		return true;
	}

	public bool Equals(Extent other)
	{
		if (MinX.Equals(other.MinX) && MaxX.Equals(other.MaxX) && MinY.Equals(other.MinY))
		{
			return MaxY.Equals(other.MaxY);
		}
		return false;
	}

	public override bool Equals(object obj)
	{
		if (obj is Extent other)
		{
			return Equals(other);
		}
		return false;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(MinX, MaxX, MinY, MaxY);
	}

	public Point PointAtFraction(double xFrac, double yFrac)
	{
		double x = Left + Width * xFrac;
		double y = Bot + Height * yFrac;
		return new Point(x, y);
	}

	public bool IsOOB(Point point)
	{
		return !Contains(point);
	}

	public Point[] GetEdgeDivisorPoints(int edgeDivisor)
	{
		Point[] points = new Point[edgeDivisor * 4];
		GetEdgeDivisorPoints(edgeDivisor, points);
		return points;
	}

	public void GetEdgeDivisorPoints(int edgeDivisor, Point[] pts)
	{
		if (pts.Length != edgeDivisor * 4)
		{
			throw new ArgumentException("pts.Length must equal edgeDivisor * 4");
		}
		for (int i = 0; i < edgeDivisor; i++)
		{
			pts[i] = new Point(UpperLeft.X * (double)(edgeDivisor - i) / (double)edgeDivisor + UpperRight.X * (double)i / (double)edgeDivisor, UpperLeft.Y * (double)(edgeDivisor - i) / (double)edgeDivisor + UpperRight.Y * (double)i / (double)edgeDivisor);
			pts[edgeDivisor + i] = new Point(UpperRight.X * (double)(edgeDivisor - i) / (double)edgeDivisor + LowerRight.X * (double)i / (double)edgeDivisor, UpperRight.Y * (double)(edgeDivisor - i) / (double)edgeDivisor + LowerRight.Y * (double)i / (double)edgeDivisor);
			pts[2 * edgeDivisor + i] = new Point(LowerRight.X * (double)(edgeDivisor - i) / (double)edgeDivisor + LowerLeft.X * (double)i / (double)edgeDivisor, LowerRight.Y * (double)(edgeDivisor - i) / (double)edgeDivisor + LowerLeft.Y * (double)i / (double)edgeDivisor);
			pts[3 * edgeDivisor + i] = new Point(LowerLeft.X * (double)(edgeDivisor - i) / (double)edgeDivisor + UpperLeft.X * (double)i / (double)edgeDivisor, LowerLeft.Y * (double)(edgeDivisor - i) / (double)edgeDivisor + UpperLeft.Y * (double)i / (double)edgeDivisor);
		}
	}

	public Point[] GetBorderPoints()
	{
		Point[] points = new Point[8];
		FillWithBorderPoints(points);
		return points;
	}

	public void FillWithBorderPoints(Span<Point> pts)
	{
		if (pts.Length != 8)
		{
			throw new ArgumentException("pts must be 8 length");
		}
		double x = (MinX + MaxX) * 0.5;
		double y = (MinY + MaxY) * 0.5;
		pts[0] = new Point(MinX, MinY);
		pts[1] = new Point(x, MinY);
		pts[2] = new Point(MaxX, MinY);
		pts[3] = new Point(MaxX, y);
		pts[4] = new Point(MaxX, MaxY);
		pts[5] = new Point(x, MaxY);
		pts[6] = new Point(MinX, MaxY);
		pts[7] = new Point(MinX, y);
	}

	public void GetBorderPoints(Span<Point> borderPoints)
	{
		if (borderPoints.Length < 8)
		{
			throw new ArgumentException("Buffer too small.");
		}
		double x = (MinX + MaxX) * 0.5;
		double y = (MinY + MaxY) * 0.5;
		borderPoints[0] = new Point(MinX, MinY);
		borderPoints[1] = new Point(x, MinY);
		borderPoints[2] = new Point(MaxX, MinY);
		borderPoints[3] = new Point(MaxX, y);
		borderPoints[4] = new Point(MaxX, MaxY);
		borderPoints[5] = new Point(x, MaxY);
		borderPoints[6] = new Point(MinX, MaxY);
		borderPoints[7] = new Point(MinX, y);
	}

	public Polygon ToPolygon()
	{
		return Polygon.FromExtent(this);
	}

	public override string ToString()
	{
		return "(" + UpperLeft.ToString() + " -> " + LowerRight.ToString() + ")";
	}
}
