using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.DataStructures;
using Geospatial.Helpers;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Rendering.Systems;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Utility;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class PointRenderLayer : VectorRenderLayer, ISelection, IBasicSelection
{
	public struct CircleSpriteDefinition
	{
		public class Comparer : IEqualityComparer<CircleSpriteDefinition>
		{
			public bool Equals(CircleSpriteDefinition x, CircleSpriteDefinition y)
			{
				return x.Equals(y);
			}

			public int GetHashCode([DisallowNull] CircleSpriteDefinition obj)
			{
				return obj.GetHashCode();
			}
		}

		public int RadiusPixels;

		public ColorBGRA FillColor;

		public ColorBGRA StrokeColor;

		public int StrokeThicknessPixels;

		public bool IsAntiAliased;

		public override bool Equals([NotNullWhen(true)] object obj)
		{
			if (!(obj is CircleSpriteDefinition csd))
			{
				return false;
			}
			return Equals(csd);
		}

		public bool Equals(CircleSpriteDefinition csd)
		{
			if (csd.RadiusPixels == RadiusPixels && (int)csd.FillColor == (int)FillColor && (int)csd.StrokeColor == (int)StrokeColor && csd.StrokeThicknessPixels == StrokeThicknessPixels)
			{
				return csd.IsAntiAliased == IsAntiAliased;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(RadiusPixels, FillColor, StrokeColor, StrokeThicknessPixels, IsAntiAliased);
		}
	}

	private readonly IReadOnlyPointCollection _points;

	private readonly object _spIdxLock = new object();

	private SpatialIndex<int> _spIdx;

	public IReadOnlyPointCollection Points => _points;

	public override Extent ValidExtent => _points.Extent;

	public override int Count => _points.Count();

	public PointRenderLayer(IEnumerable<Point> points)
	{
		_points = new PointCollection(points);
	}

	public PointRenderLayer(IList<Point> points)
	{
		_points = new PointCollection(points);
	}

	public PointRenderLayer(PointCollection points)
	{
		_points = points;
	}

	public PointRenderLayer(IReadOnlyPointCollection points)
	{
		_points = points;
	}

	protected PointRenderLayer(IList<Point> points, bool mutabilityFakeArgument)
	{
		_points = new PointCollection(points);
	}

	public override ISpatialVersion GetSpatialVersionContext()
	{
		return null;
	}

	public static RasterDefinition GetMaximalPointRenderExtent(RasterDefinition rdef, VectorSymbology symb)
	{
		int pointSizeSymbTerm = (int)Math.Round((double)symb.PointSize * VectorManagerOptions.RenderScaling / 2.0, MidpointRounding.AwayFromZero);
		int penWidthTimesRenderScaling = (int)(symb.PenWidth * VectorManagerOptions.RenderScaling);
		return rdef.ExtendRaster(pointSizeSymbTerm + penWidthTimesRenderScaling + 1);
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		if (!ValidExtent.Overlaps(plot.RasterQuery.Extent))
		{
			return false;
		}
		List<int> elements = Points.PointSpatialIndex.GetElements(GetMaximalPointRenderExtent(plot.RasterQuery, VectorSymbology));
		if (LabelFIDs)
		{
			foreach (int element in elements)
			{
				Point pt = Points[element];
				if (plot.RasterQuery.Contains(pt))
				{
					plot.AddLabel(new LazyLabelDefinition<int>((int fid) => fid.ToString(), element, pt));
				}
			}
		}
		return PaintPoints(elements, (int idx) => Points[idx], VectorSymbology, plot);
	}

	public static bool PaintPoints(IList<int> points, Func<int, Point> getPoint, VectorSymbology symbology, PlotRequest plot, bool antiAliased = true)
	{
		return PaintPoints(ConditionalVectorSymbology.Unit.Value, points, getPoint, symbology, plot, antiAliased);
	}

	public static bool PaintPoints<T>(T model, IList<int> points, Func<int, Point> getPoint, VectorSymbology symbology, PlotRequest plot, bool antiAliased = true)
	{
		if (symbology is IConditionalVectorSymbology { HasAnyOverrides: not false })
		{
			if (symbology is ConditionalVectorSymbology<T> symb)
			{
				return PaintConditionalSymbology_Internal(model, points, getPoint, symb, plot, antiAliased);
			}
			throw new InvalidOperationException("TModel type does not match conditional vector symbology type.");
		}
		return PaintUniformSymbology_Internal(points, getPoint, symbology, plot, antiAliased);
	}

	private static bool PaintUniformSymbology_Internal(IList<int> points, Func<int, Point> getPt, VectorSymbology symb, PlotRequest plot, bool antiAliased = true)
	{
		if (points.Count == 0)
		{
			return false;
		}
		ColorBGRA fillColor = symb.FillColor.ToArgb();
		ColorBGRA strokeColor = symb.PenColor.ToArgb();
		double strokeThicknessPixels = symb.PenWidth * VectorManagerOptions.RenderScaling;
		int radiusPixels = (int)Math.Round((float)(int)((double)symb.PointSize * VectorManagerOptions.RenderScaling) / 2f, MidpointRounding.AwayFromZero);
		RasterDefinition rasterDefinition = plot.RasterQuery.ExtendRaster((int)((double)symb.PointSize * VectorManagerOptions.RenderScaling) + (int)(symb.PenWidth * VectorManagerOptions.RenderScaling) / 2);
		IMemoryOwner<int> memoryOwner = null;
		try
		{
			Memory<int> mem = default;
			bool condition;
			if (symb.FillColor.A == byte.MaxValue)
			{
				condition = false;
				mem = plot.ColorBufferARGB;
			}
			else
			{
				condition = true;
				memoryOwner = SharedMemoryPool<int>.Rent(plot.RasterQuery.CellCount, out mem);
			}
			bool condition2 = false;
			int cellCount = rasterDefinition.CellCount;
			int rows = plot.RasterQuery.Rows;
			int cols = plot.RasterQuery.Cols;
			using IMemoryOwner<bool> memoryOwner2 = SharedMemoryPool<bool>.Rent(cellCount);
			Span<bool> boolSpan = memoryOwner2.Memory.Span;
			new Dictionary<CircleSpriteDefinition, int[]>(new CircleSpriteDefinition.Comparer());
			Span<ColorBGRA> colorBGRAs = ColorBGRA.From(mem.Span);
			CircleSpriteDefinition def = new CircleSpriteDefinition
			{
				RadiusPixels = radiusPixels,
				IsAntiAliased = antiAliased,
				FillColor = fillColor,
				StrokeColor = strokeColor,
				StrokeThicknessPixels = (int)strokeThicknessPixels
			};
			Span<ColorBGRA> colorBGRAsSpan = ColorBGRA.From(BuildCircleSprite(def));
			int radiusPixelsPlusStrokeThicknessPixels = def.RadiusPixels + def.StrokeThicknessPixels;
			for (int i = 0; i < points.Count; i++)
			{
				int point = points[i];
				Point pt = getPt(point);
				if (!rasterDefinition.Contains(pt))
				{
					continue;
				}
				Pixel pixel = plot.RasterQuery.CellIndex2D(pt);
				int index = rasterDefinition.CellIndex(pt);
				if (boolSpan[index])
				{
					continue;
				}
				boolSpan[index] = true;
				int unity = 1;
				int count = radiusPixelsPlusStrokeThicknessPixels * 2 - 1 + unity * 2;
				for (int j = 0; j < count; j++)
				{
					int pixelTerm = pixel.Y - radiusPixelsPlusStrokeThicknessPixels + j + 1 - unity;
					if (pixelTerm < 0)
					{
						continue;
					}
					if (pixelTerm >= rows)
					{
						break;
					}
					for (int k = 0; k < count; k++)
					{
						int pixelTerm2 = pixel.X - radiusPixelsPlusStrokeThicknessPixels + k + 1 - unity;
						if (pixelTerm2 >= 0)
						{
							if (pixelTerm2 >= cols)
							{
								break;
							}
							int index2 = pixelTerm * cols + pixelTerm2;
							colorBGRAs[index2] = colorBGRAsSpan[j * count + k].BlendOver(colorBGRAs[index2]);
							condition2 = true;
						}
					}
				}
			}
			if (condition2)
			{
				if (condition)
				{
					plot.Splat(mem);
				}
				else
				{
					plot.AlertDirectSplat();
				}
			}
			return condition2;
		}
		catch
		{
			throw;
		}
		finally
		{
			memoryOwner?.Dispose();
		}
	}

	private static bool PaintConditionalSymbology_Internal<T>(T model, IList<int> points, Func<int, Point> getPoint, ConditionalVectorSymbology<T> symb, PlotRequest plot, bool antiAliased = true)
	{
		if (points.Count == 0)
		{
			return false;
		}
		RasterDefinition rasterDefinition = plot.RasterQuery.ExtendRaster((int)((double)symb.PointSize * VectorManagerOptions.RenderScaling) + (int)(symb.PenWidth * VectorManagerOptions.RenderScaling) / 2);
		IMemoryOwner<int> memoryOwner = null;
		try
		{
			Memory<int> mem = default;
			memoryOwner = SharedMemoryPool<int>.Rent(plot.RasterQuery.CellCount, out mem);
			bool condition = false;
			int rows = plot.RasterQuery.Rows;
			int cols = plot.RasterQuery.Cols;
			Dictionary<CircleSpriteDefinition, int[]> comparer = new Dictionary<CircleSpriteDefinition, int[]>(new CircleSpriteDefinition.Comparer());
			Span<ColorBGRA> colorBGRAs = ColorBGRA.From(mem.Span);
			for (int i = 0; i < points.Count; i++)
			{
				int point = points[i];
				Point pt = getPoint(point);
				if (!rasterDefinition.Contains(pt))
				{
					continue;
				}
				ColorBGRA fillColor = symb.GetFillColorDynamic(model, point).ToArgb();
				ColorBGRA strokeColor = symb.GetPenColorDynamic(model, point).ToArgb();
				double strokeThicknessPixels = symb.GetPenWidthDynamic(model, point) * VectorManagerOptions.RenderScaling;
				int radiusPixels = (int)Math.Round((float)(int)((double)symb.GetPointSizeDynamic(model, point) * VectorManagerOptions.RenderScaling) / 2f, MidpointRounding.AwayFromZero);
				CircleSpriteDefinition circleSpriteDefinition = new CircleSpriteDefinition
				{
					RadiusPixels = radiusPixels,
					IsAntiAliased = antiAliased,
					FillColor = fillColor,
					StrokeColor = strokeColor,
					StrokeThicknessPixels = (int)strokeThicknessPixels
				};
				if (!comparer.TryGetValue(circleSpriteDefinition, out var comparerCircleSpriteDefinitionTerm))
				{
					comparerCircleSpriteDefinitionTerm = (comparer[circleSpriteDefinition] = BuildCircleSprite(circleSpriteDefinition));
				}
				Span<ColorBGRA> colorBGRAsSpan = ColorBGRA.From(comparerCircleSpriteDefinitionTerm);
				Pixel pixel = plot.RasterQuery.CellIndex2D(pt);
				int unity = 1;
				int radiusPixelsPlusStrokeThicknessPixels = circleSpriteDefinition.RadiusPixels + circleSpriteDefinition.StrokeThicknessPixels;
				int count = radiusPixelsPlusStrokeThicknessPixels * 2 - 1 + unity * 2;
				for (int j = 0; j < count; j++)
				{
					int pixelTerm = pixel.Y - radiusPixelsPlusStrokeThicknessPixels + j + 1 - unity;
					if (pixelTerm < 0)
					{
						continue;
					}
					if (pixelTerm >= rows)
					{
						break;
					}
					for (int k = 0; k < count; k++)
					{
						int pixelTerm2 = pixel.X - radiusPixelsPlusStrokeThicknessPixels + k + 1 - unity;
						if (pixelTerm2 >= 0)
						{
							if (pixelTerm2 >= cols)
							{
								break;
							}
							int index = pixelTerm * cols + pixelTerm2;
							colorBGRAs[index] = colorBGRAsSpan[j * count + k].BlendOver(colorBGRAs[index]);
							condition = true;
						}
					}
				}
			}
			if (condition)
			{
				plot.Splat(mem);
			}
			return condition;
		}
		catch
		{
			throw;
		}
		finally
		{
			memoryOwner?.Dispose();
		}
	}

	public static bool PaintUniformSymbology(PointCollection points, VectorSymbology symb, PlotRequest request)
	{
		List<int> elements = points.PointSpatialIndex.GetElements(request.RasterQuery);
		if (elements.Count == 0)
		{
			return false;
		}
		return PaintUniformSymbology_Internal(elements, (int idx) => points[idx], symb, request);
	}

	public static bool Paint(IList<Point> points, VectorSymbology symb, PlotRequest plot, bool antiAliased = true)
	{
		return PaintPoints(Enumerable.Range(0, points.Count).ToList(), (int i) => points[i], symb, plot, antiAliased);
	}

	public override bool Highlight(int idx, PlotRequest req)
	{
		return Paint(_points[idx], VectorSymbology.GetHighlightSymbology(), req);
	}

	public static int[] BuildCircleSprite(CircleSpriteDefinition def)
	{
		int unity = 1;
		int radiusPixelsPlusStrokeThicknessPixels = def.RadiusPixels + def.StrokeThicknessPixels;
		int yComponent = radiusPixelsPlusStrokeThicknessPixels * 2 - 1 + unity * 2;
		int[] circleSprite = new int[yComponent * yComponent];
		PaintCircle(queryRaster: new RasterDefinition(new Point(0.0, yComponent), 1.0, yComponent, yComponent), pt: new Point((double)radiusPixelsPlusStrokeThicknessPixels - 0.5 + (double)unity, (double)radiusPixelsPlusStrokeThicknessPixels - 0.5 + (double)unity), radiusPixels: def.RadiusPixels, buffer: circleSprite.AsSpan(), color: def.FillColor, strokeColor: def.StrokeColor, strokeThickness: def.StrokeThicknessPixels);
		return circleSprite;
	}

	private static bool PaintCircle(Point pt, int radiusPixels, RasterDefinition queryRaster, Span<int> buffer, ColorBGRA color, ColorBGRA strokeColor, double strokeThickness, bool isAntiAliased = true)
	{
		int unity = 1;
		Pixel pixel = queryRaster.CellIndex2D(pt);
		bool result = false;
		double radiusPixelsPolynomial = radiusPixels * radiusPixels;
		int radiusPixelsPlusStrokeThickness = radiusPixels + (int)strokeThickness;
		double computed = radiusPixelsPlusStrokeThickness * radiusPixelsPlusStrokeThickness;
		for (int i = pixel.Y - radiusPixelsPlusStrokeThickness + 1 - unity; i < pixel.Y + radiusPixelsPlusStrokeThickness + unity; i++)
		{
			if (i < 0)
			{
				continue;
			}
			if (i >= queryRaster.Rows)
			{
				break;
			}
			for (int j = pixel.X - radiusPixelsPlusStrokeThickness + 1 - unity; j < pixel.X + radiusPixelsPlusStrokeThickness + unity; j++)
			{
				if (j < 0)
				{
					continue;
				}
				if (j >= queryRaster.Cols)
				{
					break;
				}
				double pixelTerm = j - pixel.X;
				double pixelTerm2 = i - pixel.Y;
				double computed2 = pixelTerm * pixelTerm + pixelTerm2 * pixelTerm2;
				if (!isAntiAliased)
				{
					if (computed2 <= radiusPixelsPolynomial)
					{
						buffer[queryRaster.CellIndex(i, j)] = color;
						result = true;
					}
					else if (computed2 <= computed)
					{
						buffer[queryRaster.CellIndex(i, j)] = strokeColor;
						result = true;
					}
					continue;
				}
				double unity2 = 0.0;
				if (computed2 < radiusPixelsPolynomial)
				{
					unity2 = 1.0;
				}
				else
				{
					double radiusPixelsTerm = Math.Abs(Math.Sqrt(computed2) - (double)radiusPixels);
					if (radiusPixelsTerm < 1.0)
					{
						unity2 = 1.0 - radiusPixelsTerm;
					}
				}
				int index = queryRaster.CellIndex(i, j);
				if (unity2 < 1.0)
				{
					double thisColorPercent = 0.0;
					if (computed2 < computed)
					{
						thisColorPercent = 1.0;
					}
					else
					{
						double computed3 = Math.Abs(Math.Sqrt(computed2) - (double)radiusPixelsPlusStrokeThickness);
						if (computed3 < 1.0)
						{
							thisColorPercent = 1.0 - computed3;
						}
					}
					ColorBGRA belowColor = buffer[index];
					buffer[index] = strokeColor.BlendOver(belowColor, thisColorPercent);
					result = true;
				}
				if (unity2 > 0.0)
				{
					ColorBGRA belowColor2 = buffer[index];
					buffer[index] = color.BlendOver(belowColor2, unity2);
					result = true;
				}
			}
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool PaintSquare(Point pt, int radiusPixels, RasterDefinition queryRaster, Span<int> buffer, int argbColor)
	{
		Pixel pixel = queryRaster.CellIndex2D(pt);
		bool result = false;
		for (int i = pixel.Y - radiusPixels; i <= pixel.Y + radiusPixels; i++)
		{
			if (i < 0)
			{
				continue;
			}
			if (i >= queryRaster.Rows)
			{
				break;
			}
			for (int j = pixel.X - radiusPixels; j <= pixel.X + radiusPixels; j++)
			{
				if (j >= 0)
				{
					if (j >= queryRaster.Cols)
					{
						break;
					}
					buffer[queryRaster.CellIndex(i, j)] = argbColor;
					result = true;
				}
			}
		}
		return result;
	}

	public static bool Paint(Point point, VectorSymbology symb, PlotRequest plot)
	{
		return Paint(new Point[1] { point }, symb, plot);
	}

	private SpatialIndex<int> ComputeSpatialIndex()
	{
		return Points.PointSpatialIndex;
	}

	public override SpatialIndex<int> EnsureGetSpatialIndex()
	{
		return LazyThreadsafe<SpatialIndex<int>>.AllocateOnce(ref _spIdx, _spIdxLock, this, (PointRenderLayer t) => t.ComputeSpatialIndex());
	}

	public override bool HitTest(int candidateIdx, Point pt, double hitDistance)
	{
		return HitTest(_points, candidateIdx, pt, hitDistance);
	}

	public static bool HitTest(IEnumerable<Point> pts, int candidateIdx, Point pt, double hitDistance)
	{
		return pts.ElementAt(candidateIdx).CloseToXY(pt, hitDistance);
	}

	public override bool HitTest(int candidateIdx, Extent ext)
	{
		return HitTest(_points, candidateIdx, ext);
	}

	public static bool HitTest(IReadOnlyList<Point> pts, int candidateIdx, Extent ext)
	{
		return ext.Contains(pts[candidateIdx]);
	}

	public override bool HitTest(int candidateIdx, Polyline ext)
	{
		return ext.Intersects(_points[candidateIdx], 0.0);
	}

	public override bool HitTest(int candidateIdx, Polygon lasso, MarqueeHitMode hitMode)
	{
		return lasso.Contains(_points[candidateIdx]);
	}

	public override double DistanceTo(int idx, Point pt)
	{
		return _points[idx].DistanceTo(pt);
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + (_points?.VersionXY ?? 0);
	}

	public IReadOnlyList<Point> GetSelectedPoints()
	{
		if (SelectedSet.Count == 0)
		{
			return null;
		}
		return SelectedSet.Select((int fid) => _points[fid]).ToList();
	}
}
