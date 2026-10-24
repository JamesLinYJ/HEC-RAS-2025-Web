using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rendering.Editing.Handlers;

public static class PolylineSnapHelpers
{
	public static Polyline SnapPolyline(Polyline input, IResample<float> raster, PolylineSnapOptions options)
	{
		if (input == null)
		{
			throw new ArgumentNullException("input");
		}
		if (raster == null)
		{
			throw new ArgumentNullException("raster");
		}
		if (input.Count < 2)
		{
			return input.Clone();
		}
		Polyline polyline = input;
		for (int i = 0; i < options.Iterations; i++)
		{
			polyline = SnapOnce(polyline, raster, options);
		}
		return polyline;
	}

	public static List<Polyline> SnapPolylines(IReadOnlyList<Polyline> inputs, IResample<float> raster, PolylineSnapOptions options)
	{
		if (inputs == null)
		{
			throw new ArgumentNullException("inputs");
		}
		if (raster == null)
		{
			throw new ArgumentNullException("raster");
		}
		List<Polyline> polylines = new List<Polyline>(inputs.Count);
		for (int i = 0; i < inputs.Count; i++)
		{
			polylines.Add(SnapPolyline(inputs[i], raster, options));
		}
		return polylines;
	}

	private static Polyline SnapOnce(Polyline input, IResample<float> raster, PolylineSnapOptions options)
	{
		Memory<Point> mem;
		using (SharedMemoryPool<Point>.RentNoFill(input.Count, out mem))
		{
			Span<Point> memSpan = mem.Span;
			for (int i = 0; i < input.Count; i++)
			{
				memSpan[i] = SnapVertex(input[i], input, i, raster, options);
			}
			Memory<double> mem2;
			if (input.HasZ && input.HasM)
			{
				using (SharedMemoryPool<double>.RentNoFill(input.Count, out mem2))
				{
					Memory<double> mem3;
					using (SharedMemoryPool<double>.RentNoFill(input.Count, out mem3))
					{
						Span<double> mem2Span = mem2.Span;
						Span<double> mem3Span = mem3.Span;
						for (int j = 0; j < input.Count; j++)
						{
							mem2Span[j] = input.Z[j];
							mem3Span[j] = input.M[j];
						}
						return new Polyline(mem, mem2, mem3);
					}
				}
			}
			Memory<double> mem4;
			if (input.HasZ)
			{
				using (SharedMemoryPool<double>.RentNoFill(input.Count, out mem4))
				{
					Span<double> mem4Span = mem4.Span;
					for (int k = 0; k < input.Count; k++)
					{
						mem4Span[k] = input.Z[k];
					}
					return new Polyline(mem, mem4);
				}
			}
			return new Polyline(mem);
		}
	}

	private static Point SnapVertex(Point vertex, Polyline line, int vertexIndex, IResample<float> raster, PolylineSnapOptions options)
	{
		int searchPointCount = GetSearchPointCount(options);
		if (searchPointCount == 0)
		{
			return vertex;
		}
		Memory<Point> mem;
		using (SharedMemoryPool<Point>.RentNoFill(searchPointCount, out mem))
		{
			Memory<float> mem2;
			using (SharedMemoryPool<float>.RentNoFill(searchPointCount, out mem2))
			{
				Span<Point> memSpan = mem.Span;
				Span<float> mem2Span = mem2.Span;
				GenerateSearchPoints(vertex, line, vertexIndex, options, memSpan);
				raster.SamplePoints(mem, mem2);
				bool elevationModeOptionsTerm = options.ElevationMode == PolylineSnapElevationMode.Highest;
				float minValue = (elevationModeOptionsTerm ? float.MinValue : float.MaxValue);
				int ordinal = -1;
				float? noDataValue = raster.NoDataValue;
				for (int i = 0; i < mem2Span.Length; i++)
				{
					float mem22 = mem2Span[i];
					if ((!noDataValue.HasValue || mem22 != noDataValue.Value) && !float.IsNaN(mem22) && !float.IsInfinity(mem22) && (elevationModeOptionsTerm ? (mem22 > minValue) : (mem22 < minValue)))
					{
						minValue = mem22;
						ordinal = i;
					}
				}
				return (ordinal >= 0) ? memSpan[ordinal] : vertex;
			}
		}
	}

	private static int GetSearchPointCount(PolylineSnapOptions options)
	{
		int searchRadiusOptionsTerm = (int)Math.Ceiling(options.SearchRadius / options.SampleSpacing);
		if (options.SearchMode == PolylineSnapSearchMode.Perpendicular)
		{
			return 2 * searchRadiusOptionsTerm + 1;
		}
		return 1 + searchRadiusOptionsTerm * options.PointsPerRing;
	}

	private static void GenerateSearchPoints(Point vertex, Polyline line, int vertexIndex, PolylineSnapOptions options, Span<Point> points)
	{
		if (options.SearchMode == PolylineSnapSearchMode.Perpendicular)
		{
			Vector vector = GetLineDirectionAt(line, vertexIndex).Perpendicular(clockwise: true);
			int count = (int)Math.Ceiling(options.SearchRadius / options.SampleSpacing);
			for (int i = -count; i <= count; i++)
			{
				double sampleSpacingOptionsTerm = (double)i * options.SampleSpacing;
				double x = vertex.X + vector.DeltaX * sampleSpacingOptionsTerm;
				double y = vertex.Y + vector.DeltaY * sampleSpacingOptionsTerm;
				points[i + count] = new Point(x, y);
			}
			return;
		}
		int count2 = (int)Math.Ceiling(options.SearchRadius / options.SampleSpacing);
		points[0] = vertex;
		int unity = 1;
		for (int j = 1; j <= count2; j++)
		{
			double sampleSpacingOptionsTerm2 = (double)j * options.SampleSpacing;
			for (int k = 0; k < options.PointsPerRing; k++)
			{
				double piPointsPerRingTerm = Math.PI * 2.0 * (double)k / (double)options.PointsPerRing;
				double x2 = vertex.X + sampleSpacingOptionsTerm2 * Math.Cos(piPointsPerRingTerm);
				double y2 = vertex.Y + sampleSpacingOptionsTerm2 * Math.Sin(piPointsPerRingTerm);
				points[unity++] = new Point(x2, y2);
			}
		}
	}

	private static Vector GetLineDirectionAt(Polyline line, int vertexIndex)
	{
		if (vertexIndex == 0)
		{
			return new Segment(line[0], line[1]).UnitVector();
		}
		if (vertexIndex == line.Count - 1)
		{
			return new Segment(line[vertexIndex - 1], line[vertexIndex]).UnitVector();
		}
		return new Segment(line[vertexIndex - 1], line[vertexIndex + 1]).UnitVector();
	}
}
