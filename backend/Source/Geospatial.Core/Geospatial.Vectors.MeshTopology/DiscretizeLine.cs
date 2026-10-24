using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors.Topology;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public class DiscretizeLine
{
	private static readonly ILogger _logger = LogHelper.GetLogger("DiscretizeLine");

	public double lengthScale;

	public int count;

	public double bias = 1.0;

	public double areaWeight;

	public double slideBuffer = 0.1;

	public double slideMagnetism;

	public double discretizeMagnetism = 0.1;

	public bool followExactly;

	public bool straightDistances = true;

	public bool upsampleOnly;

	public ConstrainStretchingOptions ConstrainMode = ConstrainStretchingOptions.Floating;

	public LaplaceBCOptions LaplaceBCMode;

	public ISizeFunction sizeFunction { get; private set; }

	public SizeFunctionMode Mode
	{
		get
		{
			if (lengthScale > 0.0)
			{
				return SizeFunctionMode.Constant;
			}
			if (count > 0)
			{
				return SizeFunctionMode.CountWithBias;
			}
			if (sizeFunction != null)
			{
				return SizeFunctionMode.Tin;
			}
			return SizeFunctionMode.Unset;
		}
	}

	public static DiscretizeLine BySize(double lengthScale, double bias, double lengthTotal, bool upsampleOnly = false)
	{
		return new DiscretizeLine
		{
			upsampleOnly = upsampleOnly,
			lengthScale = lengthScale,
			bias = bias
		};
	}

	public static DiscretizeLine BySizes(double lengthScaleA, double lengthScaleB, double lengthTotal)
	{
		DiscretizeLine discretizeLine = new DiscretizeLine();
		discretizeLine.SetSizes(lengthScaleA, lengthScaleB, lengthTotal);
		return discretizeLine;
	}

	public void SetSizes(double lengthScaleA, double lengthScaleB, double lengthTotal)
	{
		count = 0;
		sizeFunction = null;
		double scaledLengthTotal = 0.99 * lengthTotal;
		if (lengthScaleA >= scaledLengthTotal || lengthScaleB >= scaledLengthTotal)
		{
			lengthScale = lengthTotal;
			bias = 1.0;
			return;
		}
		bias = lengthScaleB / lengthScaleA;
		if (Math.Abs(bias - 1.0) < 0.001)
		{
			lengthScale = lengthScaleA;
			bias = 1.0;
		}
		else
		{
			double d = (lengthTotal - lengthScaleA) / (lengthTotal - lengthScaleB);
			double biasTerm = 2.0 + Math.Log(bias) / Math.Log(d);
			lengthScale = lengthTotal / (biasTerm - 1.0);
		}
	}

	public void SetCount(int pointCount, double bias)
	{
		count = pointCount;
		this.bias = bias;
		lengthScale = 0.0;
		sizeFunction = null;
	}

	public static DiscretizeLine ByCount(int pointCount, double bias)
	{
		DiscretizeLine discretizeLine = new DiscretizeLine();
		discretizeLine.SetCount(pointCount, bias);
		return discretizeLine;
	}

	public DiscretizeLine(double lengthScale, bool upsampleOnly = false, double bias = 1.0)
	{
		this.upsampleOnly = upsampleOnly;
		this.lengthScale = lengthScale;
		this.bias = bias;
		count = 0;
		sizeFunction = null;
	}

	public DiscretizeLine(int count, double bias = 1.0)
	{
		this.count = count;
		this.bias = bias;
		lengthScale = 0.0;
	}

	public DiscretizeLine(ISizeFunction sizeFunction, bool upsampleOnly = false)
	{
		this.upsampleOnly = upsampleOnly;
		this.sizeFunction = sizeFunction;
		lengthScale = 0.0;
		count = 0;
	}

	public static DiscretizeLine Unset()
	{
		return new DiscretizeLine
		{
			lengthScale = -1.0,
			count = -1,
			sizeFunction = null
		};
	}

	public void SetSizeFunction(ISizeFunction sizeFunction)
	{
		this.sizeFunction = sizeFunction;
		lengthScale = 0.0;
		count = 0;
	}

	private DiscretizeLine()
	{
		lengthScale = 0.0;
		count = 0;
		sizeFunction = null;
	}

	public DiscretizeLine Clone()
	{
		return new DiscretizeLine
		{
			count = count,
			bias = bias,
			lengthScale = lengthScale,
			sizeFunction = sizeFunction,
			areaWeight = areaWeight,
			slideBuffer = slideBuffer,
			slideMagnetism = slideMagnetism,
			discretizeMagnetism = discretizeMagnetism,
			straightDistances = straightDistances,
			upsampleOnly = upsampleOnly,
			ConstrainMode = ConstrainMode,
			LaplaceBCMode = LaplaceBCMode,
			followExactly = followExactly
		};
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(count, bias, lengthScale, slideBuffer, slideMagnetism, discretizeMagnetism, straightDistances, LaplaceBCMode);
	}

	public bool ValidSize()
	{
		if (sizeFunction == null)
		{
			if (double.IsFinite(bias) && bias > 0.0)
			{
				if (!double.IsFinite(lengthScale) || !(lengthScale > 0.0))
				{
					return count > 1;
				}
				return true;
			}
			return false;
		}
		return true;
	}

	public Dictionary<string, object> Serialize()
	{
		return new Dictionary<string, object>
		{
			["count"] = count,
			["bias"] = bias,
			["lengthScale"] = lengthScale,
			["sizeFunction"] = sizeFunction != null
		};
	}

	public void Apply(Polyline src, ref Polyline dst, bool includeExisting = false)
	{
		if (Mode == SizeFunctionMode.Constant)
		{
			if (upsampleOnly)
			{
				UpsampleBySize(src, ref dst);
			}
			else if (includeExisting)
			{
				DiscretizeExisting(src, ref dst);
			}
			else
			{
				Discretize(src, ref dst);
			}
		}
		else if (Mode == SizeFunctionMode.Tin)
		{
			if (upsampleOnly)
			{
				UpsampleBySizeFunction(src, ref dst);
			}
			else if (includeExisting)
			{
				DiscretizeExisting(src, ref dst);
			}
			else
			{
				Discretize(src, ref dst);
			}
		}
		else if (Mode == SizeFunctionMode.CountWithBias)
		{
			if (upsampleOnly)
			{
				throw new NotImplementedException("Upsample-only with count is not implemented");
			}
			DiscretizeByCount(src, ref dst);
		}
		else if (Mode == SizeFunctionMode.Unset)
		{
			throw new ArgumentException("Null/unset DiscretizeLine cannot be applied");
		}
		if (!includeExisting && !upsampleOnly && (areaWeight > 0.0 || slideMagnetism > 0.0))
		{
			OptimizeBoundary(src, ref dst);
		}
	}

	public double FractionalCount(Polyline src)
	{
		if (Mode == SizeFunctionMode.Tin)
		{
			if (upsampleOnly)
			{
				Polyline dst = null;
				UpsampleBySize(src, ref dst);
				return dst.PointCount;
			}
			return FractionalCountBySizeFunction(src);
		}
		if (Mode == SizeFunctionMode.Constant)
		{
			if (upsampleOnly)
			{
				Polyline dst2 = null;
				UpsampleBySize(src, ref dst2);
				return dst2.PointCount;
			}
			return 1.0 + src.Length / lengthScale;
		}
		if (Mode == SizeFunctionMode.CountWithBias)
		{
			return count;
		}
		throw new ArgumentException("Count request on invalid linear discertization");
	}

	public double FractionalCountBySizeFunction(Polyline src)
	{
		double[] cumulativeSegmentLengths = src.CumulativeSegmentLengths();
		double cumulativeSegmentLengthsTerm = cumulativeSegmentLengths.Last();
		int i = 1;
		Point point = src.Points[0];
		double computed = 0.0;
		double unity = 1.0;
		for (int j = 0; j < 1000000; j++)
		{
			double cumulativeSegmentLengthsDistanceToTerm = cumulativeSegmentLengths[i - 1] - point.DistanceTo(src.Points[i]);
			double computed2 = cumulativeSegmentLengthsTerm - cumulativeSegmentLengthsDistanceToTerm;
			double query = sizeFunction.Query(point);
			double queryTerm = computed2 / query;
			if (queryTerm <= 1.0)
			{
				return unity + queryTerm;
			}
			double query2 = query;
			double query2DistanceToTerm;
			if (straightDistances)
			{
				double query2Polynomial = query2 * query2;
				double distanceSquaredTo = point.DistanceSquaredTo(src.Points[i]);
				if (distanceSquaredTo > query2Polynomial)
				{
					double distanceTo = src.Points[i].DistanceTo(src.Points[i - 1]);
					query2DistanceToTerm = computed % 1.0 + query2 / distanceTo;
				}
				else
				{
					while (i < src.Count && distanceSquaredTo < query2Polynomial)
					{
						i++;
						if (i == src.Count)
						{
							double sqrtDistanceSquaredTo = Math.Sqrt(distanceSquaredTo);
							return unity + sqrtDistanceSquaredTo / query2;
						}
						distanceSquaredTo = point.DistanceSquaredTo(src.Points[i]);
					}
					double distanceTo2 = src.Points[i - 1].DistanceTo(point);
					double distanceTo3 = src.Points[i].DistanceTo(point);
					query2DistanceToTerm = (query2 - distanceTo2) / (distanceTo3 - distanceTo2);
				}
			}
			else
			{
				double query2Term;
				for (query2Term = cumulativeSegmentLengthsDistanceToTerm + query2; cumulativeSegmentLengths[i - 1] < query2Term; i++)
				{
				}
				query2DistanceToTerm = ((i != 1) ? ((query2Term - cumulativeSegmentLengths[i - 2]) / (cumulativeSegmentLengths[i - 1] - cumulativeSegmentLengths[i - 2])) : (query2Term / cumulativeSegmentLengths[i - 1]));
			}
			computed = (double)(i - 1) + query2DistanceToTerm;
			point = src.Points[i - 1] * (1.0 - query2DistanceToTerm) + src.Points[i] * query2DistanceToTerm;
			unity++;
		}
		throw new DynamicMeshException("Fell out of FractionalCount loop");
	}

	public void StartEndSize(out double startSize, out double endSize, Polyline arc)
	{
		StartEndSize(out startSize, out endSize, arc, out var _);
	}

	public void StartEndSize(out double startSize, out double endSize, Polyline arc, out double r)
	{
		double arcLengthScaleTerm;
		if (Mode == SizeFunctionMode.Constant)
		{
			if (bias == 1.0)
			{
				r = 1.0;
				startSize = (endSize = lengthScale);
				return;
			}
			arcLengthScaleTerm = 1.0 + arc.Length / lengthScale;
		}
		else
		{
			if (Mode != SizeFunctionMode.CountWithBias)
			{
				if (Mode == SizeFunctionMode.Tin)
				{
					startSize = sizeFunction.Query(arc.Points.First());
					endSize = sizeFunction.Query(arc.Points.Last());
					r = double.NaN;
					return;
				}
				throw new Exception("Query against unset linear size function");
			}
			if (bias == 1.0)
			{
				startSize = (endSize = arc.Length / (double)(count - 1));
				r = 1.0;
				return;
			}
			arcLengthScaleTerm = count;
		}
		if (arcLengthScaleTerm <= 2.05)
		{
			startSize = (endSize = arc.Length);
			r = 1.0;
		}
		else
		{
			r = Math.Pow(bias, 1.0 / (arcLengthScaleTerm - 2.0));
			startSize = arc.Length * (1.0 - r) / (1.0 - Math.Pow(r, arcLengthScaleTerm - 1.0));
			endSize = bias * startSize;
		}
	}

	public double MeanLength(Polyline src)
	{
		if (Mode == SizeFunctionMode.Constant)
		{
			return lengthScale;
		}
		if (Mode == SizeFunctionMode.Tin)
		{
			return src.Length / Math.Max(1.0, FractionalCount(src) - 1.0);
		}
		if (Mode == SizeFunctionMode.CountWithBias)
		{
			return src.Length / ((double)count - 1.0);
		}
		throw new ArgumentException("Mean length queried on unset DiscretizeLine");
	}

	public double GetSegmentScale(Polyline src, PointOnPolyline pop, out float rate, double Lpop, bool following)
	{
		double length = src.Length;
		if (Lpop < 0.0)
		{
			Lpop = src.DistanceAlong(pop);
		}
		double r;
		if (Mode == SizeFunctionMode.Constant)
		{
			if (bias == 1.0)
			{
				rate = 1f;
				return lengthScale;
			}
			StartEndSize(out var startSize, out var _, src, out r);
			if (r == 1.0)
			{
				rate = 1f;
				return startSize;
			}
		}
		else
		{
			if (Mode != SizeFunctionMode.CountWithBias)
			{
				if (Mode == SizeFunctionMode.Tin)
				{
					rate = 1f;
					return sizeFunction.Query(pop.Point);
				}
				throw new ArgumentException("Segment size query on unset DiscretizeLine");
			}
			if (bias == 1.0)
			{
				rate = 1f;
				return length / (double)(count - 1);
			}
			if (count == 2)
			{
				rate = 1f;
				return length;
			}
			r = Math.Pow(bias, 1.0 / ((double)count - 2.0));
		}
		double lengthBiasTerm = length * (1.0 - r) / (1.0 - r * bias);
		double lpopTerm = (r - 1.0) * Lpop + lengthBiasTerm;
		if (following)
		{
			rate = (float)r;
		}
		else
		{
			lpopTerm /= r;
			rate = (float)(1.0 / r);
		}
		return lpopTerm;
	}

	public static double ComputeRemainingSteps(double nextStepSize, double remainingDistance, float rate)
	{
		if (rate == 1f)
		{
			return remainingDistance / nextStepSize;
		}
		double remainingDistanceRateTerm = Math.Log(1.0 - remainingDistance * (double)(1f - rate) / nextStepSize) / Math.Log(rate);
		if (double.IsNaN(remainingDistanceRateTerm))
		{
			throw new ArgumentException("Bias was too small, remaining distance never reached");
		}
		return remainingDistanceRateTerm;
	}

	public void DiscretizeExisting(Polyline src, ref Polyline dst)
	{
		if (dst == null)
		{
			throw new ArgumentException("Existing node string is null");
		}
		List<Point> points = new List<Point> { dst.Points[0] };
		PointOnPolyline pointOnPolyline = src.FirstPointOnPolyline();
		DiscretizeLine discretizeLine = null;
		double startSize;
		double r;
		if (Mode == SizeFunctionMode.Constant || Mode == SizeFunctionMode.CountWithBias)
		{
			StartEndSize(out startSize, out var _, src, out r);
			discretizeLine = Clone();
		}
		else
		{
			double endSize;
			startSize = (endSize = (r = double.NaN));
			discretizeLine = this;
		}
		double[] cumulativeSegmentLengths = null;
		for (int i = 0; i < dst.SegmentCount; i++)
		{
			Segment segment = dst.Segments[i];
			PointOnPolyline pointOnPolyline2 = ((i + 1 != dst.SegmentCount) ? src.NearestPointOnPolyline(segment.B) : src.LastPointOnPolyline());
			Polyline src2 = src.Subsection(pointOnPolyline, pointOnPolyline2);
			if (Mode != SizeFunctionMode.Tin)
			{
				double distanceAlongFast = src.DistanceAlongFast(pointOnPolyline, ref cumulativeSegmentLengths);
				double distanceAlongFast2 = src.DistanceAlongFast(pointOnPolyline2, ref cumulativeSegmentLengths);
				double lengthScaleA = startSize + (r - 1.0) * distanceAlongFast;
				double lengthScaleB = (startSize + (r - 1.0) * distanceAlongFast2) / r;
				discretizeLine.SetSizes(lengthScaleA, lengthScaleB, distanceAlongFast2 - distanceAlongFast);
			}
			Polyline dst2 = null;
			discretizeLine.Discretize(src2, ref dst2);
			for (int j = 1; j < dst2.PointCount - 1; j++)
			{
				points.Add(dst2.Points[j]);
			}
			points.Add(segment.B);
			pointOnPolyline = pointOnPolyline2;
		}
		dst.Clear();
		dst.AddRange(points);
	}

	public void Discretize(Polyline src, ref Polyline dst)
	{
		double[] segDistances = src.CumulativeSegmentLengths();
		double segDistancesTerm = segDistances.Last();
		StartEndSize(out var startSize, out var _, src, out var r);
		List<Point> points = new List<Point> { src.Points[0] };
		List<double> doubleList = new List<double> { 0.0 };
		int i = 1;
		Func<double, double> func;
		if (Mode == SizeFunctionMode.Constant || Mode == SizeFunctionMode.CountWithBias)
		{
			func = ComputeScaleBySize;
		}
		else
		{
			if (Mode != SizeFunctionMode.Tin)
			{
				throw new ArgumentException("Discretization not set");
			}
			func = ComputeScaleByTin;
		}
		double computed;
		do
		{
			Point other = points.Last();
			double segDistancesDistanceToTerm = segDistances[i - 1] - other.DistanceTo(src.Points[i]);
			double computed2 = segDistancesTerm - segDistancesDistanceToTerm;
			double func2 = func(segDistancesDistanceToTerm);
			int func2Term = (int)Math.Round(computed2 / func2);
			if (func2Term <= 1)
			{
				points.Add(src.Last());
				doubleList.Add((double)src.Points.Count - 1.0);
				break;
			}
			double computed3 = computed2 / (double)func2Term;
			double distanceToTerm;
			if (straightDistances)
			{
				bool condition = true;
				while (i < src.Count && other.DistanceTo(src.Points[i]) < computed3)
				{
					i++;
					condition = false;
				}
				if (i == src.Count)
				{
					points.Add(src.Last());
					doubleList.Add((double)i - 1.0);
					break;
				}
				if (condition)
				{
					double distanceTo = src.Points[i].DistanceTo(src.Points[i - 1]);
					distanceToTerm = doubleList.Last() % 1.0 + computed3 / distanceTo;
				}
				else
				{
					double distanceTo2 = src.Points[i - 1].DistanceTo(other);
					double distanceTo3 = src.Points[i].DistanceTo(other);
					distanceToTerm = (computed3 - distanceTo2) / (distanceTo3 - distanceTo2);
				}
			}
			else
			{
				double computed4;
				for (computed4 = segDistancesDistanceToTerm + computed3; segDistances[i - 1] < computed4; i++)
				{
				}
				distanceToTerm = ((i != 1) ? ((computed4 - segDistances[i - 2]) / (segDistances[i - 1] - segDistances[i - 2])) : (computed4 / segDistances[i - 1]));
			}
			Point point = src.Points[i - 1] * (1.0 - distanceToTerm) + src.Points[i] * distanceToTerm;
			computed = (double)(i - 1) + distanceToTerm;
			if (discretizeMagnetism > 0.0)
			{
				double x = Math.Abs(Math.Log(other.DistanceTo(point) / computed3));
				Point point2 = default;
				double maxValue = double.MaxValue;
				double unity = -1.0;
				double distanceTo4 = other.DistanceTo(src.Points[i]);
				if (doubleList.Last() < (double)(i - 1))
				{
					double maxValue2 = Math.Abs(Math.Log(other.DistanceTo(src.Points[i - 1]) / computed3));
					if (maxValue2 < maxValue)
					{
						maxValue = maxValue2;
						point2 = src.Points[i - 1];
						unity = i - 1;
					}
					double maxValue3 = Math.Abs(Math.Log(distanceTo4 / computed3));
					if (maxValue3 < maxValue)
					{
						maxValue = maxValue3;
						point2 = src.Points[i];
						unity = i;
					}
				}
				else
				{
					double distanceTo4Term = Math.Max(1.0, Math.Round(distanceTo4 / computed3));
					double distanceTo4Term2 = distanceTo4 / distanceTo4Term;
					double maxValue4 = Math.Abs(Math.Log(distanceTo4Term2 / computed3));
					if (maxValue4 < maxValue)
					{
						maxValue = maxValue4;
						if (distanceTo4Term == 1.0)
						{
							point2 = src.Points[i];
							unity = i;
						}
						else
						{
							double segDistance = segDistances[i - 1];
							if (i > 1)
							{
								segDistance -= segDistances[i - 2];
							}
							double segDistanceTerm = doubleList.Last() % 1.0 + distanceTo4Term2 / segDistance;
							point2 = (1.0 - segDistanceTerm) * src.Points[i - 1] + segDistanceTerm * src.Points[i];
							unity = (double)(i - 1) + segDistanceTerm;
						}
					}
				}
				if (2.0 * Math.Pow(maxValue, 2.0) < 2.0 * Math.Pow(x, 2.0) + discretizeMagnetism)
				{
					point = point2;
					computed = unity;
					i = 1 + (int)unity;
				}
			}
			points.Add(point);
			doubleList.Add(computed);
		}
		while (!(computed >= (double)(src.Points.Count - 1)));
		int ordinal = 2;
		if (src.Points[0] == src.Points.Last())
		{
			ordinal = 4;
		}
		if (points.Count < ordinal)
		{
			DiscretizeByCount(src, ref dst, ordinal, bias);
		}
		else
		{
			dst = new Polyline(points);
		}
		double ComputeScaleBySize(double s)
		{
			return startSize + (r - 1.0) * s;
		}
		double ComputeScaleByTin(double s)
		{
			return sizeFunction.Query(src.PointAtDistanceFast(s, segDistances).Point);
		}
	}

	public void UpsampleBySize(Polyline src, ref Polyline dst)
	{
		dst = new Polyline { src.Points[0] };
		if (bias == 1.0)
		{
			foreach (Segment segment in src.Segments)
			{
				UpsampleSegment(segment.A, segment.B, lengthScale, lengthScale, dst);
			}
			return;
		}
		StartEndSize(out var startSize, out var endSize, src, out var r);
		foreach (Segment segment2 in src.Segments)
		{
			endSize = startSize + (r - 1.0) * segment2.Length;
			UpsampleSegment(segment2.A, segment2.B, startSize, endSize, dst);
			startSize = endSize;
		}
	}

	public void UpsampleBySizeFunction(Polyline src, ref Polyline dst)
	{
		dst = new Polyline { src.Points[0] };
		double scaleA = sizeFunction.Query(src.Points[0]);
		foreach (Segment segment in src.Segments)
		{
			double scaleA2 = sizeFunction.Query(segment.B);
			UpsampleSegment(segment.A, segment.B, scaleA, scaleA2, dst);
			scaleA = scaleA2;
		}
	}

	public static void UpsampleSegment(Point A, Point B, double scaleA, double scaleB, Polyline dst)
	{
		double distanceTo = A.DistanceTo(B);
		double scaleAScaleBTerm = 0.5 * (scaleA + scaleB);
		int count = (int)Math.Round(distanceTo / scaleAScaleBTerm);
		if (count > 1)
		{
			if (Math.Abs(scaleA - scaleB) < 0.0001)
			{
				for (int i = 1; i < count; i++)
				{
					double countTerm = (double)i / (double)count;
					Point point = (1.0 - countTerm) * A + countTerm * B;
					if (dst.HasZ)
					{
						dst.Add(point, scaleA);
					}
					else
					{
						dst.Add(point);
					}
				}
			}
			else
			{
				double x = 1.0 + (scaleB - scaleA) / distanceTo;
				double countTerm2 = 1.0 / (1.0 - Math.Pow(x, count));
				for (int j = 1; j < count; j++)
				{
					double computed = countTerm2 * (1.0 - Math.Pow(x, j));
					Point point2 = (1.0 - computed) * A + computed * B;
					if (dst.HasZ)
					{
						dst.Add(point2, (1.0 - computed) * scaleA + computed * scaleB);
					}
					else
					{
						dst.Add(point2);
					}
				}
			}
		}
		if (dst.HasZ)
		{
			dst.Add(B, scaleB);
		}
		else
		{
			dst.Add(B);
		}
	}

	public static void DiscretizeByCount(Polyline src, ref Polyline dst, int count, double bias)
	{
		List<Point> points = new List<Point> { src.FirstPointOnPolyline().Point };
		if (bias == 1.0 || count == 2)
		{
			for (int i = 1; i < count - 1; i++)
			{
				double fractionAlongLine = (double)i / ((double)count - 1.0);
				points.Add(src.PointAtFraction(fractionAlongLine).Point);
			}
		}
		else
		{
			double biasCountTerm = Math.Pow(bias, 1.0 / ((double)count - 2.0));
			double countTerm = (1.0 - biasCountTerm) / (1.0 - Math.Pow(biasCountTerm, count - 1));
			for (int j = 1; j < count - 1; j++)
			{
				double fractionAlongLine2 = countTerm * (1.0 - Math.Pow(biasCountTerm, j)) / (1.0 - biasCountTerm);
				points.Add(src.PointAtFraction(fractionAlongLine2).Point);
			}
		}
		points.Add(src.Last());
		dst = new Polyline(points);
	}

	public void DiscretizeByCount(Polyline src, ref Polyline dst)
	{
		if (count < 0)
		{
			count = dst.Count;
		}
		DiscretizeByCount(src, ref dst, count, bias);
	}

	public double EndBearing(Polyline pl, int end)
	{
		if (upsampleOnly)
		{
			if (end == 0)
			{
				return (pl.Segments[0].Bearing + Math.PI) % (Math.PI * 2.0);
			}
			return pl.Segments.Last().Bearing;
		}
		return EndSegment(pl, end).Bearing;
	}

	public Segment EndSegment(Polyline pl, int end)
	{
		double length = pl.Length;
		StartEndSize(out var startSize, out var endSize, pl);
		if (end == 0)
		{
			return EndSegment(pl, end, startSize / length);
		}
		return EndSegment(pl, end, endSize / length);
	}

	public static Segment EndSegment(Polyline pl, int end, double fraction)
	{
		fraction = Math.Min(1.0, Math.Max(0.0, fraction));
		if (end == 0)
		{
			return new Segment(pl.PointAtFraction(fraction).Point, pl.Points.First());
		}
		return new Segment(pl.PointAtFraction(1.0 - fraction).Point, pl.Points.Last());
	}

	private void OptimizeBoundary(Polyline src, ref Polyline dst)
	{
		if (src.Points.Count <= 2)
		{
			return;
		}
		List<PointOnPolyline> pops = dst.Points.Select((Point p) => src.NearestPointOnPolyline(p)).ToList();
		if (src.First() == src.Last())
		{
			pops[pops.Count - 1] = src.LastPointOnPolyline();
		}
		int count = 25;
		double[] cachedDistance = null;
		StartEndSize(out var startSize, out var _, src, out var r);
		for (int ordinal = 0; ordinal < count; ordinal++)
		{
			int ordinal2 = 0;
			for (int pntIdx = 1; pntIdx < pops.Count - 1; pntIdx++)
			{
				double localLengthScale;
				if (Mode == SizeFunctionMode.Tin)
				{
					localLengthScale = sizeFunction.Query(pops[pntIdx].Point);
				}
				else
				{
					double distanceAlongFast = src.DistanceAlongFast(pops[pntIdx], ref cachedDistance);
					localLengthScale = startSize + (r - 1.0) * distanceAlongFast;
				}
				double distanceAlongFast2 = src.DistanceAlongFast(pops[pntIdx], ref cachedDistance);
				double slideBufferDistanceAlongFast2Term = slideBuffer * distanceAlongFast2 + (1.0 - slideBuffer) * src.DistanceAlongFast(pops[pntIdx - 1], ref cachedDistance);
				double slideBufferDistanceAlongFast2Term2 = slideBuffer * distanceAlongFast2 + (1.0 - slideBuffer) * src.DistanceAlongFast(pops[pntIdx + 1], ref cachedDistance);
				UnivariateMinimizer univariateMinimizer = new UnivariateMinimizer(NodeLoss, distanceAlongFast2, slideBufferDistanceAlongFast2Term, slideBufferDistanceAlongFast2Term2, 0.01 * localLengthScale, -1.0);
				if (!univariateMinimizer.Minimize(out var xOpt) && ordinal != 0)
				{
					continue;
				}
				PointOnPolyline pops2 = src.PointAtDistanceFast(xOpt, cachedDistance);
				int segmentIndex = pops2.SegmentIndex;
				Segment segment = src.Segments[segmentIndex];
				double cachedDistance2 = cachedDistance[segmentIndex];
				double cachedDistance2AboveLength = cachedDistance2 - segment.Length;
				double nodeLoss = ((!(cachedDistance2AboveLength >= slideBufferDistanceAlongFast2Term)) ? (univariateMinimizer.fOpt + 1000000.0) : NodeLoss(cachedDistance2AboveLength));
				double nodeLoss2 = ((!(cachedDistance2 <= slideBufferDistanceAlongFast2Term2)) ? (univariateMinimizer.fOpt + 1000000.0) : NodeLoss(cachedDistance2));
				if (nodeLoss < nodeLoss2)
				{
					if (nodeLoss < univariateMinimizer.fOpt + slideMagnetism)
					{
						pops2 = src.PointAtDistanceFast(cachedDistance2AboveLength, cachedDistance);
					}
				}
				else if (nodeLoss2 < univariateMinimizer.fOpt + slideMagnetism)
				{
					pops2 = src.PointAtDistanceFast(cachedDistance2, cachedDistance);
				}
				if (pops[pntIdx].Point != pops2.Point)
				{
					pops[pntIdx] = pops2;
					ordinal2++;
				}
				double NodeLoss(double dist)
				{
					PointOnPolyline pointOnPolyline = src.PointAtDistanceFast(dist, cachedDistance);
					double distanceTo = pops[pntIdx - 1].Point.DistanceTo(pointOnPolyline.Point);
					double distanceTo2 = pops[pntIdx + 1].Point.DistanceTo(pointOnPolyline.Point);
					double x = Math.Abs(Math.Log(distanceTo / localLengthScale)) + Math.Abs(Math.Log(distanceTo2 / localLengthScale));
					double netSegmentAreaNetSegmentArea2Term = 0.0;
					if (areaWeight > 0.0)
					{
						double netSegmentArea = NetSegmentArea(src, pops[pntIdx - 1], pointOnPolyline);
						double netSegmentArea2 = NetSegmentArea(src, pointOnPolyline, pops[pntIdx + 1]);
						double distanceToPlusDistanceTo2 = distanceTo + distanceTo2;
						netSegmentAreaNetSegmentArea2Term = (netSegmentArea + netSegmentArea2) / (distanceToPlusDistanceTo2 * distanceToPlusDistanceTo2);
						netSegmentAreaNetSegmentArea2Term = Math.Pow(netSegmentAreaNetSegmentArea2Term, 2.0);
					}
					return Math.Pow(x, 2.0) + areaWeight * netSegmentAreaNetSegmentArea2Term;
				}
			}
			if (ordinal2 == 0)
			{
				break;
			}
		}
		dst = new Polyline(pops.Select((PointOnPolyline pop) => pop.Point));
	}

	private static double NetSegmentArea(Polyline src, PointOnPolyline popA, PointOnPolyline popB)
	{
		return Math.Abs(new Polygon(src.Subsection(popA, popB)).Area);
	}

	public DiscretizeLine CloneReversed()
	{
		DiscretizeLine discretizeLine = Clone();
		discretizeLine.bias = 1.0 / bias;
		return discretizeLine;
	}
}
