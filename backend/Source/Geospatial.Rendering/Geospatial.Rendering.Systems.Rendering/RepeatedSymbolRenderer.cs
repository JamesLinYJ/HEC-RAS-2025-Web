using System;
using System.Collections.Generic;
using System.Threading;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Polylines;
using Utility.DataStructures;

namespace Geospatial.Rendering.Systems.Rendering;

public static class RepeatedSymbolRenderer
{
	private static readonly ThreadLocal<List<SymbolDefinition>> _scratchSymbols = new ThreadLocal<List<SymbolDefinition>>(() => new List<SymbolDefinition>(128), trackAllValues: false);

	private static readonly ThreadLocal<List<Segment>> _scratchChevronSegments = new ThreadLocal<List<Segment>>(() => new List<Segment>(256), trackAllValues: false);

	public static List<SymbolDefinition> GetSymbolLocationsAlongLine<K>(RasterDefinition query, K model, Func<int, K, Polyline> getPolyline, IList<FeatureSegmentSubset> candidates, double density, double smoothingDistance = 10.0)
	{
		List<SymbolDefinition> symbolDefinitions = _scratchSymbols.Value;
		symbolDefinitions.Clear();
		if (density <= 0.0)
		{
			return symbolDefinitions;
		}
		double cellSize = query.CellSize;
		double densityTimesCellSize = density * cellSize;
		double worldDistance = smoothingDistance * cellSize;
		if (densityTimesCellSize <= 0.0)
		{
			return symbolDefinitions;
		}
		Extent extent = query.Extent;
		foreach (FeatureSegmentSubset candidate in candidates)
		{
			Polyline pline = getPolyline(candidate.Fid, model);
			FastSegmentIterator fsi = new FastSegmentIterator(pline, isClosed: false);
			double computed = densityTimesCellSize * 0.5;
			CompactList<Range1D> ranges = candidate.Ranges;
			foreach (Range1D range in ranges)
			{
				for (int i = range.Start; i <= range.End; i++)
				{
					Segment segment = fsi[i];
					double length = segment.Length;
					if (length <= 0.0)
					{
						continue;
					}
					double computed2 = 0.0;
					while (computed <= length - computed2 + 1E-09)
					{
						double lengthTerm = (computed2 + computed) / length;
						Point point = segment.PointAtFraction(lengthTerm);
						Point pB = AdvanceAlongSubset(fsi, i, lengthTerm, range.End, worldDistance);
						Geospatial.Vectors.Vector direction = new Geospatial.Vectors.Vector(point, pB).UnitVector();
						if (extent.Contains(point))
						{
							symbolDefinitions.Add(new SymbolDefinition
							{
								Point = point,
								Direction = direction
							});
						}
						computed2 += computed;
						computed = densityTimesCellSize;
					}
					computed -= length - computed2;
				}
			}
		}
		return symbolDefinitions;
	}

	public static List<Segment> GetChevronsAlongLine(IReadOnlyList<SymbolDefinition> symbols, double sizeInCells, double cellSize)
	{
		List<Segment> segments = _scratchChevronSegments.Value;
		segments.EnsureCapacity(symbols.Count * 2);
		segments.Clear();
		double sizeInCellsTimesCellSize = sizeInCells * cellSize;
		foreach (SymbolDefinition symbol in symbols)
		{
			Geospatial.Vectors.Vector direction = symbol.Direction;
			Geospatial.Vectors.Vector vector = direction.Ratio(0.0 - sizeInCellsTimesCellSize);
			Geospatial.Vectors.Vector vector2 = new Geospatial.Vectors.Vector(0.0 - direction.DeltaY, direction.DeltaX).UnitVector().Ratio(sizeInCellsTimesCellSize * 1.0);
			Point ptA = symbol.Point + vector + vector2;
			Point ptA2 = symbol.Point + vector - vector2;
			segments.Add(new Segment(ptA, symbol.Point));
			segments.Add(new Segment(ptA2, symbol.Point));
		}
		return segments;
	}

	public static List<Segment> GetChevronsAlongLine<K>(RasterDefinition query, K model, Func<int, K, Polyline> getPolyline, IList<FeatureSegmentSubset> candidates, double density = 64.0, double size = 4.0, double smoothingDistance = 10.0)
	{
		return GetChevronsAlongLine(GetSymbolLocationsAlongLine(query, model, getPolyline, candidates, density, smoothingDistance), size, query.CellSize);
	}

	public static bool RenderArrowsAlongLine<K>(PlotRequest request, K model, Func<int, K, Polyline> getPolyline, IList<FeatureSegmentSubset> candidates, VectorSymbology symbology, double density = 64.0, double size = 4.0, double smoothingDistance = 10.0)
	{
		List<Segment> chevronsAlongLine = GetChevronsAlongLine(request.RasterQuery, model, getPolyline, candidates, density, size, smoothingDistance);
		if (chevronsAlongLine.Count == 0)
		{
			return false;
		}
		PolylineRenderLayer.Renderer.Render(request, chevronsAlongLine, symbology);
		return true;
	}

	private static Point AdvanceAlongSubset(FastSegmentIterator fsi, int startSegIdx, double fracOnStart, int rangeEndIdx, double worldDistance)
	{
		Segment segment = fsi[startSegIdx];
		double worldDistance2 = worldDistance;
		double segmentFracOnStartTerm = segment.Length * (1.0 - fracOnStart);
		if (worldDistance2 <= segmentFracOnStartTerm)
		{
			return segment.PointAtFraction(fracOnStart + worldDistance2 / segment.Length);
		}
		worldDistance2 -= segmentFracOnStartTerm;
		for (int i = startSegIdx + 1; i <= rangeEndIdx; i++)
		{
			if (!(worldDistance2 > 1E-09))
			{
				break;
			}
			Segment segment2 = fsi[i];
			if (worldDistance2 <= segment2.Length)
			{
				return segment2.PointAtFraction(worldDistance2 / segment2.Length);
			}
			worldDistance2 -= segment2.Length;
		}
		return fsi[rangeEndIdx].B;
	}
}
