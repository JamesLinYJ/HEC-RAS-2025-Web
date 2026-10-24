using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Geospatial.Transformers;
using Geospatial.Vectors;
using OSGeo.OSR;
using Utility.Progress;
using Utility.Unsafe;

namespace Geospatial.GDALAssist;

public sealed class ReprojectionTransformer : ISpatialTransformer, IDisposable
{
	private bool _isDisposed;

	private readonly Projection _srcProj;

	private readonly Projection _destProj;

	private readonly ThreadLocal<CoordinateTransformation> _ctCache;

	private readonly ThreadLocal<CoordinateTransformation> _reverseCtCache;

	private static readonly object ConstructorLock = new object();

	private static readonly ConcurrentDictionary<(string, string), ReprojectionTransformer> _transformerCache = new ConcurrentDictionary<(string, string), ReprojectionTransformer>();

	public bool IsReversible => true;

	public bool AllowDispose { get; private set; } = true;

	public static ReprojectionTransformer GetOrMake(Projection srcProj, Projection destProj)
	{
		string wKT = srcProj.GetWKT();
		string wKT2 = destProj.GetWKT();
		if (string.IsNullOrEmpty(wKT) || string.IsNullOrEmpty(wKT2))
		{
			return MakeCacheable(srcProj, destProj);
		}
		return _transformerCache.GetOrAdd((wKT, wKT2), ((string, string) _) => MakeCacheable(srcProj, destProj));
	}

	private static ReprojectionTransformer MakeCacheable(Projection srcProj, Projection destProj)
	{
		return new ReprojectionTransformer(srcProj, destProj)
		{
			AllowDispose = false
		};
	}

	public ReprojectionTransformer(Projection srcProj, Projection destProj)
	{
		if (srcProj == null)
		{
			throw new ArgumentNullException("srcProj", "Cannot build a reprojection from a null source projection.");
		}
		if (destProj == null)
		{
			throw new ArgumentNullException("destProj", "Cannot build a reprojection to a null destination projection.");
		}
		_srcProj = srcProj;
		_destProj = destProj;
		_ctCache = new ThreadLocal<CoordinateTransformation>(() =>
		{
			lock (ConstructorLock)
			{
				return Projection.CreateTransformation(_srcProj, _destProj);
			}
		}, trackAllValues: true);
		_reverseCtCache = new ThreadLocal<CoordinateTransformation>(() =>
		{
			lock (ConstructorLock)
			{
				return Projection.CreateTransformation(_destProj, _srcProj);
			}
		}, trackAllValues: true);
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		Projector.TransformPoints(_ctCache.Value, points.Span, transformed.Span);
	}

	public void Transform(Span<Point> points, Span<Point> transformed)
	{
		Projector.TransformPoints(_ctCache.Value, points, transformed);
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		Projector.TransformPoints(_reverseCtCache.Value, points.Span, transformed.Span);
	}

	public void ReverseTransform(Span<Point> points, Span<Point> transformed)
	{
		Projector.TransformPoints(_reverseCtCache.Value, points, transformed);
	}

	public List<ComplexPolygonCollection> Transform(IList<ComplexPolygonCollection> collections, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		List<ComplexPolygonCollection> complexPolygonCollections = new List<ComplexPolygonCollection>(collections.Count);
		for (int i = 0; i < collections.Count; i++)
		{
			complexPolygonCollections.Add(new ComplexPolygonCollection(Transform(collections[i].Polygons)));
			if (i % 100 == 0)
			{
				prog.ReportProgress(i * 100 / collections.Count);
			}
		}
		return complexPolygonCollections;
	}

	public ComplexPolygon Transform(ComplexPolygon polygon)
	{
		if (polygon == null || polygon.Rings.Count == 0)
		{
			return null;
		}
		if ((bool)ComplexPolygon.TryCreateComplexPolygon(Transform(polygon.Rings), out var poly))
		{
			return poly;
		}
		return null;
	}

	public List<ComplexPolygon> Transform(IReadOnlyList<ComplexPolygon> polygons)
	{
		List<ComplexPolygon> complexPolygons = new List<ComplexPolygon>(polygons.Count);
		for (int i = 0; i < polygons.Count; i++)
		{
			complexPolygons.Add(Transform(polygons[i]));
		}
		return complexPolygons;
	}

	public Polygon Transform(Polygon polygon)
	{
		Polygon polygon2 = Polygon.CreateWithBuffer(Transform(polygon.GetPointBufferUnsafe(), _ctCache.Value));
		if (polygon.HasZ)
		{
			polygon2.TrackZ(polygon.Z);
		}
		if (polygon.HasM)
		{
			polygon2.TrackM(polygon.M);
		}
		return polygon2;
	}

	public Polyline Transform(Polyline polyline)
	{
		Polyline polyline2 = Polyline.CreateWithBuffer(Transform(polyline.GetPointBufferUnsafe(), _ctCache.Value));
		if (polyline.HasZ)
		{
			polyline2.TrackZ(polyline.Z);
		}
		if (polyline.HasM)
		{
			polyline2.TrackM(polyline.M);
		}
		return polyline2;
	}

	public List<Polygon> Transform(IReadOnlyList<Polygon> polygons)
	{
		List<Polygon> polygonsList = new List<Polygon>(polygons.Count);
		for (int i = 0; i < polygons.Count; i++)
		{
			polygonsList.Add(Transform(polygons[i]));
		}
		return polygonsList;
	}

	public List<Polyline> Transform(IReadOnlyList<Polyline> polylines)
	{
		List<Polyline> polylinesList = new List<Polyline>(polylines.Count);
		for (int i = 0; i < polylines.Count; i++)
		{
			polylinesList.Add(Transform(polylines[i]));
		}
		return polylinesList;
	}

	private static List<Point> Transform(Memory<Point> points, CoordinateTransformation ct)
	{
		Point[] pointsArray = new Point[points.Length];
		Projector.TransformPoints(ct, points.Span, pointsArray);
		List<Point> pointsList = new List<Point>();
		UnsafeHelpers.SetListBackingBuffer(pointsList, pointsArray);
		return pointsList;
	}

	private void DisposeInternal()
	{
		foreach (CoordinateTransformation coordinateTransformation in _ctCache.Values)
		{
			coordinateTransformation?.Dispose();
		}
		foreach (CoordinateTransformation coordinateTransformation2 in _reverseCtCache.Values)
		{
			coordinateTransformation2?.Dispose();
		}
	}

	private void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				DisposeInternal();
			}
			_isDisposed = true;
		}
	}

	~ReprojectionTransformer()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		if (AllowDispose)
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}
	}
}
