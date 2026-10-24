using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.Rasters.Convolution;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;
using Utility.DataStructures;
using Utility.Memory;
using Utility.Metadata;

namespace Geospatial.Rendering.Systems.Vector;

public class VectorManager<VectorCanvas, Shape, ContiguousPolygonCollection, ContiguousMultiPolygonCollection, SegmentShape, ContiguousLineCollection, ContiguousEllipseCollection, TextShape, VectorGroup> : IVectorManager, IDisposable, IHasMetadata where VectorCanvas : class, ICanvas where Shape : class, IShape where ContiguousPolygonCollection : class, IContiguousLineCollection, Shape, new() where ContiguousMultiPolygonCollection : class, IContiguousComplexPolygonCollection, Shape, new() where SegmentShape : class, ISegment, Shape, new() where ContiguousLineCollection : class, IContiguousLineCollection, Shape, new() where ContiguousEllipseCollection : class, IContiguousEllipseCollection, Shape, new() where TextShape : class, IText, Shape, new() where VectorGroup : class, IVectorCanvas, new()
{
	public class ClearVectorDataPackage
	{
		public static NoHandlePool<ClearVectorDataPackage> Pool = new NoHandlePool<ClearVectorDataPackage>(() => new ClearVectorDataPackage());

		public VectorManager<VectorCanvas, Shape, ContiguousPolygonCollection, ContiguousMultiPolygonCollection, SegmentShape, ContiguousLineCollection, ContiguousEllipseCollection, TextShape, VectorGroup> VectorManager;

		public VectorIDData<Shape, VectorGroup> Data;

		public long CachedRenderState;

		public void Clear()
		{
			VectorManager = null;
			Data = null;
			Pool.Return(this);
		}
	}

	public class RedrawVectorDataPackage<K, L>
	{
		public static NoHandlePool<RedrawVectorDataPackage<K, L>> Pool = new NoHandlePool<RedrawVectorDataPackage<K, L>>(() => new RedrawVectorDataPackage<K, L>());

		public VectorManager<VectorCanvas, Shape, ContiguousPolygonCollection, ContiguousMultiPolygonCollection, SegmentShape, ContiguousLineCollection, ContiguousEllipseCollection, TextShape, VectorGroup> VectorManager;

		public VectorIDData<Shape, VectorGroup> Data;

		public long CachedRenderState;

		public int TotalVectors;

		public Func<int, Extent, L, int> GetNumberOfRequiredShapes;

		public Func<Shape> InitializeShapeFunc;

		public L GetNumbRequiredShapesModel;

		public Action<Memory<Shape>, int, K, Matrix3x3D> UpdateShapeFunc;

		public K UpdateShapeModel;

		public Matrix3x3D Transform;

		public void Clear()
		{
			VectorManager = null;
			Data = null;
			GetNumberOfRequiredShapes = null;
			InitializeShapeFunc = null;
			GetNumbRequiredShapesModel = default;
			UpdateShapeFunc = null;
			UpdateShapeModel = default;
			Pool.Return(this);
		}
	}

	public class UpdateSymbologyDataPackage
	{
		public static NoHandlePool<UpdateSymbologyDataPackage> Pool = new NoHandlePool<UpdateSymbologyDataPackage>(() => new UpdateSymbologyDataPackage());

		public VectorManager<VectorCanvas, Shape, ContiguousPolygonCollection, ContiguousMultiPolygonCollection, SegmentShape, ContiguousLineCollection, ContiguousEllipseCollection, TextShape, VectorGroup> VectorManager;

		public VectorIDData<Shape, VectorGroup> Data;

		public void Clear()
		{
			VectorManager = null;
			Data = null;
			Pool.Return(this);
		}
	}

	public class DeleteVectorDataPackage
	{
		public static NoHandlePool<DeleteVectorDataPackage> Pool = new NoHandlePool<DeleteVectorDataPackage>(() => new DeleteVectorDataPackage());

		public VectorManager<VectorCanvas, Shape, ContiguousPolygonCollection, ContiguousMultiPolygonCollection, SegmentShape, ContiguousLineCollection, ContiguousEllipseCollection, TextShape, VectorGroup> VectorManager;

		public VectorIDData<Shape, VectorGroup> Data;

		public VectorID ID;

		public long CachedRenderState;

		public void Clear()
		{
			VectorManager = null;
			Data = null;
			Pool.Return(this);
		}
	}

	public class SetVectorZIndexDataPackage
	{
		public static NoHandlePool<SetVectorZIndexDataPackage> Pool = new NoHandlePool<SetVectorZIndexDataPackage>(() => new SetVectorZIndexDataPackage());

		public VectorManager<VectorCanvas, Shape, ContiguousPolygonCollection, ContiguousMultiPolygonCollection, SegmentShape, ContiguousLineCollection, ContiguousEllipseCollection, TextShape, VectorGroup> VectorManager;

		public VectorIDData<Shape, VectorGroup> Data;

		public int ZIndex;

		public void Clear()
		{
			VectorManager = null;
			Data = null;
			Pool.Return(this);
		}
	}

	private VectorCanvas _canvas;

	private Func<Extent> _getVisibleWorldExtent;

	private readonly IDictionary<VectorID, VectorIDData<Shape, VectorGroup>> _vectorData;

	private int _onscreenShapeCount;

	private long _vecIdCount = 1L;

	private bool _isDisposed;

	private Matrix3x3D _baseTransform;

	private Matrix3x3D _transformationFromBase = Matrix3x3D.Identity;

	private Matrix3x3D _lastDynamicVectorUpdateTransformation = Matrix3x3D.Identity;

	private Extent _lastDynamicVectorUpdateExtent = Extent.NullExtent;

	private SortedList<int, Shape[]> _shapeArrayCache = new SortedList<int, Shape[]>();

	private int _shapeArrayCacheMaxSize = 250;

	private static NoHandlePool<VectorGroup> _vectorGroupPool = new NoHandlePool<VectorGroup>(() => new VectorGroup(), 1000)
	{
		MaxResidentAllowed = 1000,
		OnEvicted = (VectorGroup vg) =>
		{
			vg.Dispose();
		}
	};

	private static NoHandlePool<ContiguousLineCollection> _lineCollectionPool = new NoHandlePool<ContiguousLineCollection>(() => new ContiguousLineCollection(), 5000)
	{
		MaxResidentAllowed = 5000,
		OnEvicted = (ContiguousLineCollection contiguousLineCollection) =>
		{
			contiguousLineCollection.Dispose();
		}
	};

	private static NoHandlePool<ContiguousPolygonCollection> _polygonCollectionPool = new NoHandlePool<ContiguousPolygonCollection>(() => new ContiguousPolygonCollection(), 5000)
	{
		MaxResidentAllowed = 5000,
		OnEvicted = (ContiguousPolygonCollection contiguousPolygonCollection) =>
		{
			contiguousPolygonCollection.Dispose();
		}
	};

	private static NoHandlePool<ContiguousMultiPolygonCollection> _complexPolygonCollectionPool = new NoHandlePool<ContiguousMultiPolygonCollection>(() => new ContiguousMultiPolygonCollection(), 5000)
	{
		MaxResidentAllowed = 5000,
		OnEvicted = (ContiguousMultiPolygonCollection contiguousMultiPolygonCollection) =>
		{
			contiguousMultiPolygonCollection.Dispose();
		}
	};

	private static NoHandlePool<ContiguousEllipseCollection> _ellipseCollectionPool = new NoHandlePool<ContiguousEllipseCollection>(() => new ContiguousEllipseCollection(), 5000)
	{
		MaxResidentAllowed = 5000,
		OnEvicted = (ContiguousEllipseCollection contiguousEllipseCollection) =>
		{
			contiguousEllipseCollection.Dispose();
		}
	};

	private static NoHandlePool<TextShape> _textPool = new NoHandlePool<TextShape>(() => new TextShape(), 750)
	{
		MaxResidentAllowed = 750,
		OnEvicted = (TextShape textShape) =>
		{
			textShape.Dispose();
		}
	};

	private static NoHandlePool<SegmentShape> _segmentPool = new NoHandlePool<SegmentShape>(() => new SegmentShape(), 10000)
	{
		MaxResidentAllowed = 10000
	};

	public VectorCanvas Canvas => _canvas;

	ICanvas IVectorManager.Canvas => _canvas;

	public int VectorIDCount => _vectorData.Count;

	public int VectorCount => _vectorData.Values.Sum((VectorIDData<Shape, VectorGroup> vectorIDData) => vectorIDData.CurrentlyUsedShapeCache.Length);

	public bool IsDisposed => _isDisposed;

	public Matrix3x3D BaseTransform => _baseTransform;

	public Matrix3x3D TransformationFromBase
	{
		get
		{
			return _transformationFromBase;
		}
		set
		{
			if (!_isDisposed)
			{
				if (value.M11 == 0.0 && value.M12 == 0.0 && value.M13 == 0.0)
				{
					Debugger.Break();
				}
				_transformationFromBase = value;
				SetCanvasTransformation();
				TransformationFromBaseChanged?.Invoke(value);
				TryUpdateDynamicVectors();
			}
		}
	}

	public event IVectorManager.TransformationFromBaseChangedEvent TransformationFromBaseChanged;

	public event IHasMetadata.MetadataUpdatedEvent MetadataUpdated
	{
		add
		{
		}
		remove
		{
		}
	}

	public VectorManager(VectorCanvas canvas, Func<Extent> getVisibleWorldExtent, Matrix3x3D baseTransformation)
	{
		_canvas = canvas;
		_getVisibleWorldExtent = getVisibleWorldExtent;
		_baseTransform = baseTransformation;
		TransformationFromBase = Matrix3x3D.Identity;
		_vectorData = new Dictionary<VectorID, VectorIDData<Shape, VectorGroup>>(new VectorID.Comparer());
	}

	~VectorManager()
	{
		if (!_isDisposed)
		{
			Dispose(disposing: false);
		}
	}

	public void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		if (disposing)
		{
			Canvas?.Dispose(disposing);
			foreach (VectorIDData<Shape, VectorGroup> vectorIDData in _vectorData.Values)
			{
				DeleteVector_NoQueue(vectorIDData, vectorIDData.ID);
			}
		}
		else
		{
			foreach (VectorIDData<Shape, VectorGroup> vectorIDData2 in _vectorData.Values)
			{
				DeleteVector_CleanupResources(vectorIDData2, vectorIDData2.ID);
			}
		}
		_shapeArrayCache?.Clear();
		_getVisibleWorldExtent = null;
		_isDisposed = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	private void QueueChange(Action<object> action, object model)
	{
		action?.Invoke(model);
		Canvas.Invalidate();
	}

	private void SetCanvasTransformation()
	{
		_canvas.Transformation = TransformationFromBase * BaseTransform.Invert();
		_canvas.TransformationFromBase = TransformationFromBase;
		_canvas.BaseTransform = BaseTransform;
	}

	public void AddOrUpdateVectors(IList<object> vectors, VectorSymbology symbology, ref VectorID pointsID, ref VectorID segmentsID, ref VectorID polylinesID, ref VectorID polygonsID, ref VectorID complexPolygonsID)
	{
		ParseVectors(vectors, out var points, out var segments, out var polylines, out var polygons, out var complexPolygons);
		if (points != null)
		{
			AddOrUpdateVector(ref pointsID, points, symbology);
		}
		else
		{
			TryClearVector(ref pointsID);
		}
		if (segments != null)
		{
			AddOrUpdateVector(ref segmentsID, segments, symbology);
		}
		else
		{
			TryClearVector(ref segmentsID);
		}
		if (polylines != null)
		{
			AddOrUpdateVector(ref polylinesID, polylines, symbology);
		}
		else
		{
			TryClearVector(ref polylinesID);
		}
		if (polygons != null)
		{
			AddOrUpdateVector(ref polygonsID, polygons, symbology);
		}
		else
		{
			TryClearVector(ref polygonsID);
		}
		if (complexPolygons != null)
		{
			AddOrUpdateVector(ref complexPolygonsID, complexPolygons, symbology);
		}
		else
		{
			TryClearVector(ref complexPolygonsID);
		}
	}

	public void AddOrUpdateVector(ref VectorID id, IReadOnlyList<ILabelDefinition> iLabelDefinitions, VectorSymbology symb, VectorType type = VectorType.Text)
	{
		if (id.IsNull())
		{
			id = AddTextVector(iLabelDefinitions, symb);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateTextVector(id, iLabelDefinitions);
	}

	public void AddOrUpdateVector(ref VectorID id, ILabelDefinition iLabelDefinition, VectorSymbology symb, VectorType type = VectorType.Text)
	{
		if (id.IsNull())
		{
			id = AddTextVector(iLabelDefinition, symb);
			return;
		}
		UpdateTextVector(id, iLabelDefinition);
		TryUpdateSymbology(id, symb);
	}

	public void AddOrUpdateVector(ref VectorID id, Point point, VectorSymbology symb, VectorType type = VectorType.Point)
	{
		if (id.IsNull())
		{
			id = AddVector(point, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, point);
	}

	public void AddOrUpdateVector(ref VectorID id, Memory<Point> point, VectorSymbology symb, VectorType type = VectorType.Point)
	{
		if (id.IsNull())
		{
			id = AddVector(point, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, point);
	}

	public void AddOrUpdateVector(ref VectorID id, IReadOnlyList<Point> points, VectorSymbology symb, VectorType type = VectorType.Point)
	{
		if (id.IsNull())
		{
			id = AddVector(points, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, points);
	}

	public void AddOrUpdateVector(ref VectorID id, Segment segment, VectorSymbology symb, VectorType type = VectorType.Segment)
	{
		if (id.IsNull())
		{
			id = AddVector(segment, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, segment);
	}

	public void AddOrUpdateVector(ref VectorID id, IReadOnlyList<Segment> segments, VectorSymbology symb, VectorType type = VectorType.Segment)
	{
		if (id.IsNull())
		{
			id = AddVector(segments, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, segments);
	}

	public void AddOrUpdateVector(ref VectorID id, Polyline polyline, VectorSymbology symb, VectorType type = VectorType.Line)
	{
		if (id.IsNull())
		{
			id = AddVector(polyline, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, polyline);
	}

	public void AddOrUpdateVector(ref VectorID id, IReadOnlyList<Polyline> polylines, VectorSymbology symb, VectorType type = VectorType.Line)
	{
		if (id.IsNull())
		{
			id = AddVector(polylines, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, polylines);
	}

	public void AddOrUpdateVector(ref VectorID id, Polygon polygon, VectorSymbology symb, VectorType type = VectorType.Polygon)
	{
		if (id.IsNull())
		{
			id = AddVector(polygon, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, polygon);
	}

	public void AddOrUpdateVector(ref VectorID id, IReadOnlyList<Polygon> polygons, VectorSymbology symb, VectorType type = VectorType.Polygon)
	{
		if (id.IsNull())
		{
			id = AddVector(polygons, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, polygons);
	}

	public void AddOrUpdateVector(ref VectorID id, ComplexPolygon polygon, VectorSymbology symb, VectorType type = VectorType.ComplexPolygon)
	{
		if (id.IsNull())
		{
			id = AddVector(polygon, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, polygon);
	}

	public void AddOrUpdateVector(ref VectorID id, IReadOnlyList<ComplexPolygon> polygons, VectorSymbology symb, VectorType type = VectorType.ComplexPolygon)
	{
		if (id.IsNull())
		{
			id = AddVector(polygons, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, polygons);
	}

	public void AddOrUpdateVector(ref VectorID id, IReadOnlyList<Memory<Point>> listOfPts, VectorSymbology symb, VectorType type = VectorType.Line)
	{
		if (id.IsNull())
		{
			id = AddVector(listOfPts, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, listOfPts);
	}

	public void AddOrUpdateVector(ref VectorID id, IReadOnlyList<ImmutableMultiPartPolygon> listOfPts, VectorSymbology symb, VectorType type = VectorType.ComplexPolygon)
	{
		if (id.IsNull())
		{
			id = AddVector(listOfPts, symb, type);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdateVector(id, listOfPts);
	}

	public void AddOrUpdateVector(ref VectorID id, TriangulatedPolygonData data, VectorSymbology symb)
	{
		if (id.IsNull())
		{
			id = AddPreTriangulatedPolygonVector(data, symb);
			return;
		}
		TryUpdateSymbology(id, symb);
		UpdatePreTriangulatedPolygonVector(id, data);
	}

	public void TryClearVector(ref VectorID id)
	{
		if (id.IsNull() || !_vectorData.TryGetValue(id, out var localValue))
		{
			return;
		}
		if (localValue.QueuedForDelete)
		{
			throw new InvalidOperationException("Clearing deleted vector.");
		}
		ClearCachedData(id);
		localValue.IncrementRenderState(VectorActionType.Clear);
		long renderState = localValue.RenderState;
		ClearVectorDataPackage clearVectorDataPackage = ClearVectorDataPackage.Pool.Acquire();
		clearVectorDataPackage.VectorManager = this;
		clearVectorDataPackage.Data = localValue;
		clearVectorDataPackage.CachedRenderState = renderState;
		QueueChange((object model) =>
		{
			ClearVectorDataPackage model2 = (model as ClearVectorDataPackage) ?? throw new ArgumentException();
			long cachedRenderState = model2.CachedRenderState;
			VectorIDData<Shape, VectorGroup> data = model2.Data;
			VectorManager<VectorCanvas, Shape, ContiguousPolygonCollection, ContiguousMultiPolygonCollection, SegmentShape, ContiguousLineCollection, ContiguousEllipseCollection, TextShape, VectorGroup> vectorManager = model2.VectorManager;
			model2.Clear();
			if (cachedRenderState == data.RenderState)
			{
				vectorManager.ValidateVectorMapContains(data.ID);
				Memory<Shape> currentlyUsedShapeCache = vectorManager.UpdateShapeCache(data, 0, null);
				data.CurrentlyUsedShapeCache = currentlyUsedShapeCache;
			}
		}, clearVectorDataPackage);
	}

	public void TryDeleteVector(ref VectorID id)
	{
		if (!id.IsNull())
		{
			if (_vectorData.ContainsKey(id))
			{
				DeleteVector(id);
			}
			id = 0L;
		}
	}

	public void TryDeleteVector(VectorID id)
	{
		if (!id.IsNull() && _vectorData.ContainsKey(id))
		{
			DeleteVector(id);
		}
	}

	public void TryUpdateSymbology(VectorID id, VectorSymbology symb)
	{
		if (!id.IsNull() && _vectorData.ContainsKey(id))
		{
			UpdateSymbology(_vectorData[id], symb);
		}
	}

	public VectorID AddTextVector(ILabelDefinition iLabelDefinition, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Text);
		UpdateTextVector(vectorID, iLabelDefinition);
		return vectorID;
	}

	public VectorID AddTextVector(IReadOnlyList<ILabelDefinition> iLabelDefinitions, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Text);
		UpdateTextVector(vectorID, iLabelDefinitions);
		return vectorID;
	}

	public VectorID AddPointVector(Point point, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, point);
		return vectorID;
	}

	public VectorID AddPointVector(IReadOnlyList<Point> points, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, points);
		return vectorID;
	}

	public VectorID AddPointVector(double x, double y, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, x, y);
		return vectorID;
	}

	public VectorID AddPointVector(IReadOnlyList<double> xs, IReadOnlyList<double> ys, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, xs, ys);
		return vectorID;
	}

	public VectorID AddPointVector(Segment seg, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, seg);
		return vectorID;
	}

	public VectorID AddPointVector(IReadOnlyList<Segment> segments, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, segments);
		return vectorID;
	}

	public VectorID AddPointVector(Polyline pl, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, pl);
		return vectorID;
	}

	public VectorID AddPointVector(IReadOnlyList<Polyline> lines, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, lines);
		return vectorID;
	}

	public VectorID AddPointVector(Polygon polygon, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, polygon);
		return vectorID;
	}

	public VectorID AddPointVector(IReadOnlyList<Polygon> polygons, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, polygons);
		return vectorID;
	}

	public VectorID AddPointVector(Memory<Point> points, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Point);
		UpdatePointVector(vectorID, points);
		return vectorID;
	}

	public VectorID AddSegmentVector(Point point, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, point);
		return vectorID;
	}

	public VectorID AddSegmentVector(double x, double y, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, x, y);
		return vectorID;
	}

	public VectorID AddSegmentVector(IReadOnlyList<Point> points, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, points);
		return vectorID;
	}

	public VectorID AddSegmentVector(double x1, double y1, double x2, double y2, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, new Segment(x1, y1, x2, y2));
		return vectorID;
	}

	public VectorID AddSegmentVector(IReadOnlyList<double> xs, IReadOnlyList<double> ys, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, xs, ys);
		return vectorID;
	}

	public VectorID AddSegmentVector(Segment seg, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, seg);
		return vectorID;
	}

	public VectorID AddSegmentVector(IReadOnlyList<Segment> segments, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, segments);
		return vectorID;
	}

	public VectorID AddSegmentVector(Polyline line, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, line);
		return vectorID;
	}

	public VectorID AddSegmentVector(IReadOnlyList<Polyline> lines, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, lines);
		return vectorID;
	}

	public VectorID AddSegmentVector(Polygon polygon, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, polygon);
		return vectorID;
	}

	public VectorID AddSegmentVector(IReadOnlyList<Polygon> polygons, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Segment);
		UpdateSegmentVector(vectorID, polygons);
		return vectorID;
	}

	public VectorID AddLineVector(Point point, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Line);
		UpdateLineVector(vectorID, point);
		return vectorID;
	}

	public VectorID AddLineVector(double x, double y, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Line);
		UpdateLineVector(vectorID, x, y);
		return vectorID;
	}

	public VectorID AddLineVector(IReadOnlyList<Point> points, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Line);
		UpdateLineVector(vectorID, points);
		return vectorID;
	}

	public VectorID AddLineVector(IReadOnlyList<double> xs, IReadOnlyList<double> ys, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Line);
		UpdateLineVector(vectorID, xs, ys);
		return vectorID;
	}

	public VectorID AddLineVector(Polyline line, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Line);
		UpdateLineVector(vectorID, line);
		return vectorID;
	}

	public VectorID AddLineVector(IReadOnlyList<Polyline> lines, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Line);
		UpdateLineVector(vectorID, lines);
		return vectorID;
	}

	public VectorID AddLineVector(Polygon polygon, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Line);
		UpdateLineVector(vectorID, polygon);
		return vectorID;
	}

	public VectorID AddLineVector(IReadOnlyList<Polygon> polygons, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Line);
		UpdateLineVector(vectorID, polygons);
		return vectorID;
	}

	public VectorID AddLineVector(IReadOnlyList<Memory<Point>> lines, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Line);
		UpdateLineVector(vectorID, lines);
		return vectorID;
	}

	public VectorID AddLineVector(Memory<Point> line, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Line);
		UpdateLineVector(vectorID, line);
		return vectorID;
	}

	public VectorID AddPolygonVector(Point point, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Polygon);
		UpdatePolygonVector(vectorID, point);
		return vectorID;
	}

	public VectorID AddPolygonVector(double x, double y, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Polygon);
		UpdatePolygonVector(vectorID, x, y);
		return vectorID;
	}

	public VectorID AddPolygonVector(IReadOnlyList<Point> points, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Polygon);
		UpdatePolygonVector(vectorID, points);
		return vectorID;
	}

	public VectorID AddPolygonVector(IReadOnlyList<double> xs, IReadOnlyList<double> ys, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Polygon);
		UpdatePolygonVector(vectorID, xs, ys);
		return vectorID;
	}

	public VectorID AddPolygonVector(Polyline line, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Polygon);
		UpdatePolygonVector(vectorID, line);
		return vectorID;
	}

	public VectorID AddPolygonVector(IReadOnlyList<Polyline> lines, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Polygon);
		UpdatePolygonVector(vectorID, lines);
		return vectorID;
	}

	public VectorID AddPolygonVector(Polygon polygon, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Polygon);
		UpdatePolygonVector(vectorID, polygon);
		return vectorID;
	}

	public VectorID AddPolygonVector(IReadOnlyList<Polygon> polygons, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Polygon);
		UpdatePolygonVector(vectorID, polygons);
		return vectorID;
	}

	public VectorID AddComplexPolygonVector(ComplexPolygon polygon, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.ComplexPolygon);
		UpdateComplexPolygonVector(vectorID, polygon);
		return vectorID;
	}

	public VectorID AddComplexPolygonVector(IReadOnlyList<ComplexPolygon> polygons, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.ComplexPolygon);
		UpdateComplexPolygonVector(vectorID, polygons);
		return vectorID;
	}

	public VectorID AddComplexPolygonVector(IReadOnlyList<ImmutableMultiPartPolygon> polygons, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.ComplexPolygon);
		UpdateComplexPolygonVector(vectorID, polygons);
		return vectorID;
	}

	public VectorID AddPolygonVector(IReadOnlyList<Memory<Point>> lines, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Polygon);
		UpdatePolygonVector(vectorID, lines);
		return vectorID;
	}

	public VectorID AddPreTriangulatedPolygonVector(TriangulatedPolygonData data, VectorSymbology symbology)
	{
		VectorID vectorID = InternalAddVector(symbology, VectorType.Polygon);
		UpdatePreTriangulatedPolygonVector(vectorID, data);
		return vectorID;
	}

	public void AddVectors(List<object> vectors, VectorSymbology symbology, out VectorID pointsID, out VectorID segmentsID, out VectorID polylinesID, out VectorID polygonsID, out VectorID complexPolygonsID)
	{
		ParseVectors(vectors, out var points, out var segments, out var polylines, out var polygons, out var complexPolygons);
		if (points != null)
		{
			pointsID = AddVector(points, symbology);
		}
		else
		{
			pointsID = 0L;
		}
		if (segments != null)
		{
			segmentsID = AddVector(segments, symbology);
		}
		else
		{
			segmentsID = 0L;
		}
		if (polylines != null)
		{
			polylinesID = AddVector(polylines, symbology);
		}
		else
		{
			polylinesID = 0L;
		}
		if (polygons != null)
		{
			polygonsID = AddVector(polygons, symbology);
		}
		else
		{
			polygonsID = 0L;
		}
		if (complexPolygons != null)
		{
			complexPolygonsID = AddVector(complexPolygons, symbology);
		}
		else
		{
			complexPolygonsID = 0L;
		}
	}

	public VectorID AddVector(Point point, VectorSymbology symbology, VectorType type = VectorType.Point)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(point, symbology), 
			VectorType.Segment => AddSegmentVector(point, symbology), 
			VectorType.Line => AddLineVector(point, symbology), 
			VectorType.Polygon => AddPolygonVector(point, symbology), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(IReadOnlyList<Point> points, VectorSymbology symbology, VectorType type = VectorType.Point)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(points, symbology), 
			VectorType.Segment => AddSegmentVector(points, symbology), 
			VectorType.Line => AddLineVector(points, symbology), 
			VectorType.Polygon => AddPolygonVector(points, symbology), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(double x, double y, VectorSymbology symbology, VectorType type = VectorType.Point)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(x, y, symbology), 
			VectorType.Segment => AddSegmentVector(x, y, symbology), 
			VectorType.Line => AddLineVector(x, y, symbology), 
			VectorType.Polygon => AddPolygonVector(x, y, symbology), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(IReadOnlyList<double> xs, IReadOnlyList<double> ys, VectorSymbology symbology, VectorType type = VectorType.Point)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(xs, ys, symbology), 
			VectorType.Segment => AddSegmentVector(xs, ys, symbology), 
			VectorType.Line => AddLineVector(xs, ys, symbology), 
			VectorType.Polygon => AddPolygonVector(xs, ys, symbology), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(Segment seg, VectorSymbology symbology, VectorType type = VectorType.Segment)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(seg, symbology), 
			VectorType.Segment => AddSegmentVector(seg, symbology), 
			VectorType.Line => throw new NotSupportedException(), 
			VectorType.Polygon => throw new NotSupportedException(), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(IReadOnlyList<Segment> segments, VectorSymbology symbology, VectorType type = VectorType.Segment)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(segments, symbology), 
			VectorType.Segment => AddSegmentVector(segments, symbology), 
			VectorType.Line => throw new NotSupportedException(), 
			VectorType.Polygon => throw new NotSupportedException(), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(Polyline pl, VectorSymbology symbology, VectorType type = VectorType.Line)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(pl, symbology), 
			VectorType.Segment => AddSegmentVector(pl, symbology), 
			VectorType.Line => AddLineVector(pl, symbology), 
			VectorType.Polygon => AddPolygonVector(pl, symbology), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(IReadOnlyList<Polyline> lines, VectorSymbology symbology, VectorType type = VectorType.Line)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(lines, symbology), 
			VectorType.Segment => AddSegmentVector(lines, symbology), 
			VectorType.Line => AddLineVector(lines, symbology), 
			VectorType.Polygon => AddPolygonVector(lines, symbology), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(Polygon polygon, VectorSymbology symbology, VectorType type = VectorType.Polygon)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(polygon, symbology), 
			VectorType.Segment => AddSegmentVector(polygon, symbology), 
			VectorType.Line => AddLineVector(polygon, symbology), 
			VectorType.Polygon => AddPolygonVector(polygon, symbology), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(IReadOnlyList<Polygon> polygons, VectorSymbology symbology, VectorType type = VectorType.Polygon)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(polygons, symbology), 
			VectorType.Segment => AddSegmentVector(polygons, symbology), 
			VectorType.Line => AddLineVector(polygons, symbology), 
			VectorType.Polygon => AddPolygonVector(polygons, symbology), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(ComplexPolygon polygon, VectorSymbology symbology, VectorType type = VectorType.ComplexPolygon)
	{
		if (type == VectorType.ComplexPolygon)
		{
			return AddComplexPolygonVector(polygon, symbology);
		}
		throw new NotImplementedException();
	}

	public VectorID AddVector(IReadOnlyList<ComplexPolygon> polygons, VectorSymbology symbology, VectorType type = VectorType.ComplexPolygon)
	{
		if (type == VectorType.ComplexPolygon)
		{
			return AddComplexPolygonVector(polygons, symbology);
		}
		throw new NotImplementedException();
	}

	public VectorID AddVector(IReadOnlyList<Memory<Point>> lines, VectorSymbology symbology, VectorType type = VectorType.Line)
	{
		return type switch
		{
			VectorType.Line => AddLineVector(lines, symbology), 
			VectorType.Polygon => AddPolygonVector(lines, symbology), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(Memory<Point> line, VectorSymbology symbology, VectorType type = VectorType.Line)
	{
		return type switch
		{
			VectorType.Point => AddPointVector(line, symbology), 
			VectorType.Line => AddLineVector(line, symbology), 
			_ => throw new NotImplementedException(), 
		};
	}

	public VectorID AddVector(IReadOnlyList<ImmutableMultiPartPolygon> polygon, VectorSymbology symbology, VectorType type = VectorType.ComplexPolygon)
	{
		if (type == VectorType.ComplexPolygon)
		{
			return AddComplexPolygonVector(polygon, symbology);
		}
		throw new NotImplementedException();
	}

	private VectorID InternalAddVector(VectorSymbology symbology, VectorType type)
	{
		long vecIdCountTerm = ++_vecIdCount;
		VectorGroup initializeVectorGroup = InitializeVectorGroup();
		VectorIDData<Shape, VectorGroup> vectorIDData = new VectorIDData<Shape, VectorGroup>(vecIdCountTerm, symbology, initializeVectorGroup, type);
		_vectorData[vecIdCountTerm] = vectorIDData;
		_canvas.AddChild(initializeVectorGroup);
		SetVectorZIndex(vecIdCountTerm, vectorIDData.ZIndex);
		UpdateSymbology(vectorIDData, symbology);
		return vecIdCountTerm;
	}

	private void AddShapeToScreen(VectorIDData<Shape, VectorGroup> data, Shape shape)
	{
		data.Group.AddShape(shape);
		_onscreenShapeCount++;
	}

	private static TextShape InitializeText()
	{
		if (VectorManagerOptions.USE_VECTOR_POOLING)
		{
			TextShape acquire = _textPool.Acquire();
			acquire.Label = null;
			return acquire;
		}
		return new TextShape();
	}

	private static VectorGroup InitializeVectorGroup()
	{
		if (VectorManagerOptions.USE_VECTOR_POOLING)
		{
			VectorGroup acquire = _vectorGroupPool.Acquire();
			acquire.DisposeInternalResources();
			acquire.ClipExtent = Memory<Point>.Empty;
			return acquire;
		}
		return new VectorGroup();
	}

	private static ContiguousEllipseCollection InitializeEllipseCollection()
	{
		if (VectorManagerOptions.USE_VECTOR_POOLING)
		{
			ContiguousEllipseCollection acquire = _ellipseCollectionPool.Acquire();
			acquire.DisposeInternalResources();
			return acquire;
		}
		return new ContiguousEllipseCollection();
	}

	private static SegmentShape InitializeSegment()
	{
		if (VectorManagerOptions.USE_VECTOR_POOLING)
		{
			return _segmentPool.Acquire();
		}
		return new SegmentShape();
	}

	private static ContiguousLineCollection InitializePolylineCollection()
	{
		if (VectorManagerOptions.USE_VECTOR_POOLING)
		{
			ContiguousLineCollection acquire = _lineCollectionPool.Acquire();
			acquire.DisposeInternalResources();
			return acquire;
		}
		return new ContiguousLineCollection();
	}

	private static ContiguousPolygonCollection InitializePolygonCollection()
	{
		if (VectorManagerOptions.USE_VECTOR_POOLING)
		{
			ContiguousPolygonCollection acquire = _polygonCollectionPool.Acquire();
			acquire.DisposeInternalResources();
			return acquire;
		}
		return new ContiguousPolygonCollection();
	}

	private static ContiguousMultiPolygonCollection InitializeComplexPolygonCollection()
	{
		if (VectorManagerOptions.USE_VECTOR_POOLING)
		{
			ContiguousMultiPolygonCollection acquire = _complexPolygonCollectionPool.Acquire();
			acquire.DisposeInternalResources();
			return acquire;
		}
		return new ContiguousMultiPolygonCollection();
	}

	public void UpdateTextVector(VectorID id, ILabelDefinition iLabelDefinition)
	{
		UpdateCachedData(id, iLabelDefinition);
		RedrawTextVector(id, iLabelDefinition);
	}

	public void UpdateTextVector(VectorID id, IReadOnlyList<ILabelDefinition> iLabelDefinitions)
	{
		UpdateCachedData(id, iLabelDefinitions);
		RedrawTextVector(id, iLabelDefinitions);
	}

	public void UpdatePointVector(VectorID id, Point pt)
	{
		UpdateCachedData(id, pt);
		RedrawPointVector(id, pt);
	}

	public void UpdatePointVector(VectorID id, IReadOnlyList<Point> points)
	{
		UpdateCachedData(id, points);
		RedrawPointVector<IReadOnlyList<Point>>(id, points);
	}

	public void UpdatePointVector(VectorID id, double x, double y)
	{
		Point point = new Point(x, y);
		UpdateCachedData(id, point);
		RedrawPointVector(id, point);
	}

	public void UpdatePointVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys)
	{
		PointCollection pointCollection = new PointCollection(xs, ys);
		UpdateCachedData(id, pointCollection);
		RedrawPointVector(id, pointCollection);
	}

	public void UpdatePointVector(VectorID id, Segment seg)
	{
		throw new NotImplementedException();
	}

	public void UpdatePointVector(VectorID id, IReadOnlyList<Segment> segments)
	{
		throw new NotImplementedException();
	}

	public void UpdatePointVector(VectorID id, Polyline line)
	{
		UpdateCachedData(id, line);
		RedrawPointVector(id, line);
	}

	public void UpdatePointVector(VectorID id, IReadOnlyList<Polyline> lines)
	{
		UpdateCachedData(id, lines);
		RedrawPointVector(id, lines);
	}

	public void UpdatePointVector(VectorID id, Polygon polygon)
	{
		UpdateCachedData(id, polygon);
		RedrawPointVector(id, polygon);
	}

	public void UpdatePointVector(VectorID id, Memory<Point> pts)
	{
		UpdateCachedData(id, pts);
		RedrawPointVector(id, pts);
	}

	public void UpdatePointVector(VectorID id, IReadOnlyList<Polygon> polygons)
	{
		UpdateCachedData(id, polygons);
		RedrawPointVector(id, polygons);
	}

	public void UpdateSegmentVector(VectorID id, Point point)
	{
		UpdateCachedData(id, point);
		RedrawSegmentVector(id, point);
	}

	public void UpdateSegmentVector(VectorID id, IReadOnlyList<Point> points)
	{
		UpdateCachedData(id, points);
		RedrawSegmentVector(id, points);
	}

	public void UpdateSegmentVector(VectorID id, double x, double y)
	{
		Point point = new Point(x, y);
		UpdateCachedData(id, point);
		RedrawSegmentVector(id, point);
	}

	public void UpdateSegmentVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys)
	{
		PointCollection pointCollection = new PointCollection(xs, ys);
		UpdateCachedData(id, pointCollection);
		RedrawSegmentVector(id, (IReadOnlyList<Point>)pointCollection);
	}

	public void UpdateSegmentVector(VectorID id, Segment seg)
	{
		UpdateCachedData(id, seg);
		RedrawSegmentVector(id, seg);
	}

	public void UpdateSegmentVector(VectorID id, IReadOnlyList<Segment> segments)
	{
		UpdateCachedData(id, segments);
		RedrawSegmentVector<IReadOnlyList<Segment>>(id, segments);
	}

	public void UpdateSegmentVector(VectorID id, Polyline pl)
	{
		UpdateCachedData(id, pl);
		RedrawSegmentVector(id, pl);
	}

	public void UpdateSegmentVector(VectorID id, IReadOnlyList<Polyline> polylines)
	{
		UpdateCachedData(id, polylines);
		RedrawSegmentVector(id, polylines);
	}

	public void UpdateSegmentVector(VectorID id, Polygon poly)
	{
		UpdateCachedData(id, poly);
		RedrawSegmentVector(id, poly);
	}

	public void UpdateSegmentVector(VectorID id, IReadOnlyList<Polygon> polygons)
	{
		UpdateCachedData(id, polygons);
		RedrawSegmentVector(id, polygons);
	}

	public void UpdateLineVector(VectorID id, Point pt)
	{
		Polyline polyline = new Polyline { pt };
		UpdateCachedData(id, polyline);
		RedrawLineVector(id, polyline);
	}

	public void UpdateLineVector(VectorID id, IReadOnlyList<Point> points)
	{
		Polyline polyline = new Polyline(points);
		UpdateCachedData(id, polyline);
		RedrawLineVector(id, polyline);
	}

	public void UpdateLineVector(VectorID id, double x, double y)
	{
		Polyline polyline = new Polyline
		{
			new Point(x, y)
		};
		UpdateCachedData(id, polyline);
		RedrawLineVector(id, polyline);
	}

	public void UpdateLineVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys)
	{
		Polyline polyline = Polyline.CreateWithBuffer(new PointCollection(xs, ys));
		UpdateCachedData(id, polyline);
		RedrawLineVector(id, polyline);
	}

	public void UpdateLineVector(VectorID id, Polyline line)
	{
		UpdateCachedData(id, line);
		RedrawLineVector(id, line);
	}

	public void UpdateLineVector(VectorID id, IReadOnlyList<Polyline> lines)
	{
		UpdateCachedData(id, lines);
		RedrawLineVector(id, lines);
	}

	public void UpdateLineVector(VectorID id, Polygon polygon)
	{
		UpdateCachedData(id, polygon);
		RedrawLineVector(id, polygon);
	}

	public void UpdateLineVector(VectorID id, IReadOnlyList<Polygon> polygons)
	{
		UpdateCachedData(id, polygons);
		RedrawLineVector(id, polygons);
	}

	public void UpdateLineVector(VectorID id, IReadOnlyList<Memory<Point>> lines)
	{
		UpdateCachedData(id, lines);
		RedrawLineVector(id, lines);
	}

	public void UpdateLineVector(VectorID id, Memory<Point> line)
	{
		UpdateCachedData(id, line);
		RedrawLineVector(id, line);
	}

	public void UpdatePolygonVector(VectorID id, Point point)
	{
		Polygon t = new Polygon { point };
		UpdateCachedData(id, t);
		RedrawPolygonVector(id, t);
	}

	public void UpdatePolygonVector(VectorID id, IReadOnlyList<Point> points)
	{
		Polygon t = new Polygon(points);
		UpdateCachedData(id, t);
		RedrawPolygonVector(id, t);
	}

	public void UpdatePolygonVector(VectorID id, double x, double y)
	{
		Polygon t = new Polygon
		{
			new Point(x, y)
		};
		UpdateCachedData(id, t);
		RedrawPolygonVector(id, t);
	}

	public void UpdatePolygonVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys)
	{
		Polygon t = Polygon.CreateWithBuffer(new PointCollection(xs, ys));
		UpdateCachedData(id, t);
		RedrawPolygonVector(id, t);
	}

	public void UpdatePolygonVector(VectorID id, Polyline pl)
	{
		Polygon t = new Polygon(pl);
		UpdateCachedData(id, t);
		RedrawPolygonVector(id, t);
	}

	public void UpdatePolygonVector(VectorID id, IReadOnlyList<Polyline> polylines)
	{
		Polygon[] polygons = new Polygon[polylines.Count];
		for (int i = 0; i < polylines.Count; i++)
		{
			polygons[i] = new Polygon(polylines[i]);
		}
		UpdateCachedData(id, polygons);
		RedrawPolygonVector(id, (IReadOnlyList<Polygon>)polygons);
	}

	public void UpdatePolygonVector(VectorID id, Polygon polygon)
	{
		UpdateCachedData(id, polygon);
		RedrawPolygonVector(id, polygon);
	}

	public void UpdatePolygonVector(VectorID id, IReadOnlyList<Polygon> polygons)
	{
		UpdateCachedData(id, polygons);
		RedrawPolygonVector(id, polygons);
	}

	public void UpdateComplexPolygonVector(VectorID id, ComplexPolygon polygon)
	{
		UpdateCachedData(id, polygon);
		RedrawComplexPolygonVector(id, polygon);
	}

	public void UpdateComplexPolygonVector(VectorID id, IReadOnlyList<ComplexPolygon> polygons)
	{
		UpdateCachedData(id, polygons);
		RedrawComplexPolygonVector(id, polygons);
	}

	public void UpdateComplexPolygonVector(VectorID id, IReadOnlyList<ImmutableMultiPartPolygon> polygons)
	{
		UpdateCachedData(id, polygons);
		RedrawComplexPolygonVector(id, polygons);
	}

	public void UpdatePolygonVector(VectorID id, IReadOnlyList<Memory<Point>> polygons)
	{
		UpdateCachedData(id, polygons);
		RedrawPolygonVector(id, polygons);
	}

	public void UpdatePreTriangulatedPolygonVector(VectorID id, TriangulatedPolygonData data)
	{
		UpdateCachedData(id, data);
		RedrawPreTriangulatedPolygonVector(id, data);
	}

	public void UpdateVectors(List<object> vectors, ref VectorID pointsID, ref VectorID segmentsID, ref VectorID polylinesID, ref VectorID polygonsID, ref VectorID complexPolygonsID)
	{
		ParseVectors(vectors, out var points, out var segments, out var polylines, out var polygons, out var complexPolygons);
		UpdateVector(pointsID, points ?? new List<Point>());
		UpdateVector(segmentsID, segments ?? new List<Segment>());
		UpdateVector(polylinesID, polylines ?? new List<Polyline>());
		UpdateVector(polygonsID, polygons ?? new List<Polygon>());
		UpdateVector(complexPolygonsID, complexPolygons ?? new List<ComplexPolygon>());
	}

	private void UpdateVector(VectorID id, Point point)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			UpdatePointVector(id, point);
			break;
		case VectorType.Segment:
			UpdateSegmentVector(id, point);
			break;
		case VectorType.Line:
			UpdateLineVector(id, point);
			break;
		case VectorType.Polygon:
			UpdatePolygonVector(id, point);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, IReadOnlyList<Point> points)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			UpdatePointVector(id, points);
			break;
		case VectorType.Segment:
			UpdateSegmentVector(id, points);
			break;
		case VectorType.Line:
			UpdateLineVector(id, points);
			break;
		case VectorType.Polygon:
			UpdatePolygonVector(id, points);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, double x, double y)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			UpdatePointVector(id, x, y);
			break;
		case VectorType.Segment:
			UpdateSegmentVector(id, x, y);
			break;
		case VectorType.Line:
			UpdateLineVector(id, x, y);
			break;
		case VectorType.Polygon:
			UpdatePolygonVector(id, x, y);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			UpdatePointVector(id, xs, ys);
			break;
		case VectorType.Segment:
			UpdateSegmentVector(id, xs, ys);
			break;
		case VectorType.Line:
			UpdateLineVector(id, xs, ys);
			break;
		case VectorType.Polygon:
			UpdatePolygonVector(id, xs, ys);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, Segment seg)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			UpdatePointVector(id, seg);
			break;
		case VectorType.Segment:
			UpdateSegmentVector(id, seg);
			break;
		case VectorType.Line:
			throw new NotSupportedException();
		case VectorType.Polygon:
			throw new NotSupportedException();
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, IReadOnlyList<Segment> segments)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			UpdatePointVector(id, segments);
			break;
		case VectorType.Segment:
			UpdateSegmentVector(id, segments);
			break;
		case VectorType.Line:
			throw new NotSupportedException();
		case VectorType.Polygon:
			throw new NotSupportedException();
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, Polyline pl)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			UpdatePointVector(id, pl);
			break;
		case VectorType.Segment:
			UpdateSegmentVector(id, pl);
			break;
		case VectorType.Line:
			UpdateLineVector(id, pl);
			break;
		case VectorType.Polygon:
			UpdatePolygonVector(id, pl);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, IReadOnlyList<Polyline> lines)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			UpdatePointVector(id, lines);
			break;
		case VectorType.Segment:
			UpdateSegmentVector(id, lines);
			break;
		case VectorType.Line:
			UpdateLineVector(id, lines);
			break;
		case VectorType.Polygon:
			UpdatePolygonVector(id, lines);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, Polygon polygon)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			UpdatePointVector(id, polygon);
			break;
		case VectorType.Segment:
			UpdateSegmentVector(id, polygon);
			break;
		case VectorType.Line:
			UpdateLineVector(id, polygon);
			break;
		case VectorType.Polygon:
			UpdatePolygonVector(id, polygon);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, IReadOnlyList<Polygon> polygons)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			UpdatePointVector(id, polygons);
			break;
		case VectorType.Segment:
			UpdateSegmentVector(id, polygons);
			break;
		case VectorType.Line:
			UpdateLineVector(id, polygons);
			break;
		case VectorType.Polygon:
			UpdatePolygonVector(id, polygons);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, ComplexPolygon polygon)
	{
		if (_vectorData[id].Type == VectorType.ComplexPolygon)
		{
			UpdateComplexPolygonVector(id, polygon);
			return;
		}
		throw new NotImplementedException();
	}

	public void UpdateVector(VectorID id, IReadOnlyList<ComplexPolygon> polygons)
	{
		if (_vectorData[id].Type == VectorType.ComplexPolygon)
		{
			UpdateComplexPolygonVector(id, polygons);
			return;
		}
		throw new NotImplementedException();
	}

	public void UpdateVector(VectorID id, IReadOnlyList<Memory<Point>> lines)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Line:
			UpdateLineVector(id, lines);
			break;
		case VectorType.Polygon:
			UpdatePolygonVector(id, lines);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	public void UpdateVector(VectorID id, Memory<Point> line)
	{
		if (_vectorData[id].Type == VectorType.Line)
		{
			UpdateLineVector(id, line);
			return;
		}
		throw new NotImplementedException();
	}

	public void UpdateVector(VectorID id, IReadOnlyList<ImmutableMultiPartPolygon> lines)
	{
		if (_vectorData[id].Type == VectorType.ComplexPolygon)
		{
			UpdateComplexPolygonVector(id, lines);
			return;
		}
		throw new NotImplementedException();
	}

	private void RedrawTextVector(VectorID id, ILabelDefinition iLabelDefinition)
	{
		InternalRedrawVector(id, iLabelDefinition, InitializeText, (Shape shape, ILabelDefinition iLabelDefinition2, Action<Shape, ILabelDefinition, Matrix3x3D> payload, Matrix3x3D transform) =>
		{
			payload(shape, iLabelDefinition2, transform);
		}, UpdateText, GetOnScreenTexts);
	}

	private void RedrawTextVector(VectorID id, IReadOnlyList<ILabelDefinition> iLabelDefinitions)
	{
		_ = _vectorData[id];
		InternalRedrawVector(id, iLabelDefinitions, InitializeText, (Shape shape, ILabelDefinition iLabelDefinition, Action<Shape, ILabelDefinition, Matrix3x3D> payload, Matrix3x3D transform) =>
		{
			payload(shape, iLabelDefinition, transform);
		}, UpdateText, GetOnScreenTexts);
	}

	private static void UpdateText(Shape shape, ILabelDefinition label, Matrix3x3D transform)
	{
		if (!(shape is TextShape textShape))
		{
			throw new ArgumentException("Type mismatch.");
		}
		textShape.DataTransform = transform;
		textShape.Label = label;
	}

	private void RedrawPointVector(VectorID id, Point pt)
	{
		int pointSize = _vectorData[id].Symbology.PointSize;
		InternalRedrawVector(id, new CompactList<Point>(pt), InitializeEllipseCollection, (Shape shape, IReadOnlyList<Point> pts, (Action<Shape, IReadOnlyList<Point>, int, Matrix3x3D>, int) payload, Matrix3x3D transform) =>
		{
			payload.Item1(shape, pts, payload.Item2, transform);
		}, (UpdateEllipses<IReadOnlyList<Point>>, pointSize), (IReadOnlyList<Point> var, Extent ext) => 1);
	}

	private void RedrawPointVector<T>(VectorID id, T points) where T : IReadOnlyList<Point>
	{
		int pointSize = _vectorData[id].Symbology.PointSize;
		InternalRedrawVector(id, points, InitializeEllipseCollection, (Shape shape, T pts, (Action<Shape, T, int, Matrix3x3D>, int) payload, Matrix3x3D transform) =>
		{
			payload.Item1(shape, pts, payload.Item2, transform);
		}, (UpdateEllipses, pointSize), (T var, Extent ext) => 1);
	}

	private void RedrawPointVector<T>(VectorID id, IReadOnlyList<T> points) where T : IReadOnlyList<Point>
	{
		int pointSize = _vectorData[id].Symbology.PointSize;
		InternalRedrawVector(id, points, InitializeEllipseCollection, (Shape shapes, IReadOnlyList<T> iReadOnlyList, (Action<Shape, IReadOnlyList<T>, int, Matrix3x3D>, int) payload, Matrix3x3D transform) =>
		{
			payload.Item1(shapes, iReadOnlyList, payload.Item2, transform);
		}, (UpdateEllipses, pointSize), (IReadOnlyList<T> var, Extent ext) => 1);
	}

	private void RedrawPointVector(VectorID id, Memory<Point> pts)
	{
		int pointSize = _vectorData[id].Symbology.PointSize;
		InternalRedrawVector(id, pts, InitializeEllipseCollection, (Shape shapes, Memory<Point> points, (Action<Shape, Memory<Point>, int, Matrix3x3D>, int) payload, Matrix3x3D transform) =>
		{
			payload.Item1(shapes, points, payload.Item2, transform);
		}, (UpdateEllipses, pointSize), (Memory<Point> var, Extent ext) => 1);
	}

	private static void UpdateEllipses(Shape shape, Memory<Point> pts, int pointSize, Matrix3x3D transform)
	{
		if (!(shape is ContiguousEllipseCollection contiguousEllipseCollection))
		{
			throw new ArgumentException("Type mismatch.");
		}
		contiguousEllipseCollection.Width = pointSize;
		contiguousEllipseCollection.Height = pointSize;
		contiguousEllipseCollection.ClearAndInitialize(pts.Length);
		Span<Point> ptsSpan = pts.Span;
		Span<Point> points = contiguousEllipseCollection.Points.Span;
		for (int i = 0; i < pts.Length; i++)
		{
			points[i] = transform.Transform(ptsSpan[i]);
		}
	}

	private static void UpdateEllipses<T>(Shape shape, T pts, int pointSize, Matrix3x3D transform) where T : IReadOnlyList<Point>
	{
		UpdateEllipses(shape, (IReadOnlyList<T>)new CompactList<T>(pts), pointSize, transform);
	}

	private static void UpdateEllipses<T>(Shape shape, IReadOnlyList<T> ellipseSets, int pointSize, Matrix3x3D transform) where T : IReadOnlyList<Point>
	{
		if (!(shape is ContiguousEllipseCollection contiguousEllipseCollection))
		{
			throw new ArgumentException("Type mismatch.");
		}
		contiguousEllipseCollection.Width = pointSize;
		contiguousEllipseCollection.Height = pointSize;
		int ellipseSetCountSum = 0;
		for (int i = 0; i < ellipseSets.Count; i++)
		{
			ellipseSetCountSum += ellipseSets[i].Count;
		}
		contiguousEllipseCollection.ClearAndInitialize(ellipseSetCountSum);
		Span<Point> points = contiguousEllipseCollection.Points.Span;
		int ordinal = 0;
		for (int j = 0; j < ellipseSets.Count; j++)
		{
			T ellipseSet = ellipseSets[j];
			int ordinal2 = 0;
			while (ordinal2 < ellipseSet.Count)
			{
				points[ordinal] = transform.Transform(ellipseSet[ordinal2]);
				ordinal2++;
				ordinal++;
			}
		}
	}

	private void RedrawSegmentVector(VectorID id, Point point)
	{
		throw new NotImplementedException();
	}

	private void RedrawSegmentVector(VectorID id, IReadOnlyList<Point> points)
	{
		throw new NotImplementedException();
	}

	private void RedrawSegmentVector(VectorID id, double x, double y)
	{
		throw new NotImplementedException();
	}

	private void RedrawSegmentVector(VectorID id, IReadOnlyList<double> xs, IReadOnlyList<double> ys)
	{
		throw new NotImplementedException();
	}

	private void RedrawSegmentVector(VectorID id, Segment seg)
	{
		InternalRedrawVector(id, seg, InitializeSegment, UpdateSegment, GetOnScreenSegments);
	}

	private void RedrawSegmentVector<T>(VectorID id, T segments) where T : IReadOnlyList<Segment>
	{
		InternalRedrawVector(id, segments, InitializeSegment, UpdateSegment, GetOnScreenSegments);
	}

	private void RedrawSegmentVector<T>(VectorID id, IReadOnlyList<T> ts) where T : IReadOnlyList<Segment>
	{
		throw new NotImplementedException();
	}

	private static void UpdateSegment(Shape shape, Segment segment, Matrix3x3D transform)
	{
		if (!(shape is SegmentShape segmentShape))
		{
			throw new ArgumentException("Type mismatch.");
		}
		Segment segment2 = new Segment(transform.Transform(segment.A), transform.Transform(segment.B));
		segmentShape.Segment = segment2;
	}

	private void RedrawLineVector<T>(VectorID id, T line) where T : Polyline
	{
		InternalRedrawVector(id, line, InitializePolylineCollection, UpdatePolylineCollection, (T localValue, Extent ext) => 1);
	}

	private void RedrawLineVector<T>(VectorID id, IReadOnlyList<T> lines) where T : IReadOnlyList<Point>
	{
		InternalRedrawVector(id, lines, InitializePolylineCollection, UpdatePolylineCollection, (IReadOnlyList<T> iReadOnlyList, Extent ext) => 1);
	}

	private void RedrawLineVector(VectorID id, IReadOnlyList<Memory<Point>> lines)
	{
		InternalRedrawVector(id, lines, InitializePolylineCollection, UpdatePolylineCollection, (IReadOnlyList<Memory<Point>> iReadOnlyList, Extent ext) => 1);
	}

	private void RedrawLineVector(VectorID id, Memory<Point> line)
	{
		InternalRedrawVector(id, line, InitializePolylineCollection, UpdatePolylineCollection, (Memory<Point> points, Extent ext) => 1);
	}

	private static void UpdatePolylineCollection<T>(Shape shape, T pl, Matrix3x3D transform) where T : IReadOnlyList<Point>
	{
		UpdatePolylineCollection(shape, (IReadOnlyList<T>)new CompactList<T>(pl), transform);
	}

	private static void UpdatePolylineCollection<T>(Shape shape, IReadOnlyList<T> pl, Matrix3x3D transform) where T : IReadOnlyList<Point>
	{
		if (!(shape is ContiguousLineCollection contiguousLineCollection))
		{
			throw new ArgumentException("Type mismatch");
		}
		contiguousLineCollection.SetLines(pl);
		Span<Point> lines = contiguousLineCollection.Lines.Span;
		for (int i = 0; i < contiguousLineCollection.PointCount; i++)
		{
			lines[i] = transform.Transform(lines[i]);
		}
	}

	private static void UpdatePolylineCollection(Shape shape, Memory<Point> pl, Matrix3x3D transform)
	{
		UpdatePolylineCollection(shape, (IReadOnlyList<Memory<Point>>)new CompactList<Memory<Point>>(pl), transform);
	}

	private static void UpdatePolylineCollection(Shape shape, IReadOnlyList<Memory<Point>> pl, Matrix3x3D transform)
	{
		if (!(shape is ContiguousLineCollection contiguousLineCollection))
		{
			throw new ArgumentException("Type mismatch");
		}
		contiguousLineCollection.SetLines(pl);
		Span<Point> lines = contiguousLineCollection.Lines.Span;
		for (int i = 0; i < contiguousLineCollection.PointCount; i++)
		{
			lines[i] = transform.Transform(lines[i]);
		}
	}

	private void RedrawPolygonVector<T>(VectorID id, T t) where T : Polygon
	{
		InternalRedrawVector(id, t, InitializePolygonCollection, UpdatePolylineCollection, GetOnScreenPolygons);
	}

	private void RedrawPolygonVector<T>(VectorID id, IReadOnlyList<T> polygons) where T : Polygon
	{
		InternalRedrawVector(id, polygons, (Func<Shape>)InitializePolygonCollection, (Action<Shape, T, Matrix3x3D>)UpdatePolylineCollection, (Func<T, Extent, int>)GetOnScreenPolygons);
	}

	private void RedrawPolygonVector(VectorID id, IReadOnlyList<Memory<Point>> polygons)
	{
		InternalRedrawVector(id, polygons, InitializePolygonCollection, UpdatePolylineCollection, (IReadOnlyList<Memory<Point>> mem, Extent ext) => 1);
	}

	private void RedrawPreTriangulatedPolygonVector(VectorID id, TriangulatedPolygonData data)
	{
		InternalRedrawVector(id, data, InitializePolygonCollection, UpdateTriangulatedPolygonCollection, (TriangulatedPolygonData triangulatedPolygonData, Extent ext) => 1);
	}

	private static void UpdateTriangulatedPolygonCollection(Shape shape, TriangulatedPolygonData data, Matrix3x3D transform)
	{
		IPreTriangulatablePolygonCollection shape2 = (shape as IPreTriangulatablePolygonCollection) ?? throw new ArgumentException("Type mismatch");
		shape2.SetTriangulatedData(data.Vertices, data.Indices);
		Span<Point> triangulatedVertices = shape2.TriangulatedVertices.Span;
		for (int i = 0; i < triangulatedVertices.Length; i++)
		{
			triangulatedVertices[i] = transform.Transform(triangulatedVertices[i]);
		}
	}

	private void RedrawComplexPolygonVector<T>(VectorID id, T t) where T : ComplexPolygon
	{
		InternalRedrawVector(id, t, InitializeComplexPolygonCollection, UpdateComplexPolygons, (T pl, Extent ext) => 1);
	}

	private void RedrawComplexPolygonVector<T>(VectorID id, IReadOnlyList<T> polygons) where T : ComplexPolygon
	{
		InternalRedrawVector(id, polygons, InitializeComplexPolygonCollection, UpdateComplexPolygons, (IReadOnlyList<T> pl, Extent ext) => 1);
	}

	private void RedrawComplexPolygonVector(VectorID id, IReadOnlyList<ImmutableMultiPartPolygon> polygons)
	{
		if (double.IsNaN(polygons[0].Points.Span[0].X))
		{
			throw new InvalidOperationException("Trying to set bad polygon data");
		}
		InternalRedrawVector(id, polygons, InitializeComplexPolygonCollection, UpdateComplexPolygons, (IReadOnlyList<ImmutableMultiPartPolygon> pl, Extent ext) => 1);
	}

	private static void UpdateComplexPolygons(Shape shape, IReadOnlyList<ImmutableMultiPartPolygon> pl, Matrix3x3D transform)
	{
		if (!(shape is ContiguousMultiPolygonCollection contiguousMultiPolygonCollection))
		{
			throw new ArgumentException("Type mismatch.");
		}
		if (pl.Count == 0)
		{
			contiguousMultiPolygonCollection.SetComplexPolygons(default(CompactList<ImmutableMultiPartPolygon>));
			return;
		}
		contiguousMultiPolygonCollection.SetComplexPolygons(pl);
		Span<Point> points = contiguousMultiPolygonCollection.Points.Span;
		for (int i = 0; i < contiguousMultiPolygonCollection.PointCount; i++)
		{
			points[i] = transform.Transform(points[i]);
		}
	}

	private static void UpdateComplexPolygons(Shape shape, ComplexPolygon pl, Matrix3x3D transform)
	{
		UpdateComplexPolygons(shape, new CompactList<ComplexPolygon>(pl), transform);
	}

	private static void UpdateComplexPolygons<T>(Shape shape, IReadOnlyList<T> pl, Matrix3x3D transform) where T : ComplexPolygon
	{
		if (!(shape is ContiguousMultiPolygonCollection contiguousMultiPolygonCollection))
		{
			throw new ArgumentException("Type mismatch.");
		}
		contiguousMultiPolygonCollection.SetComplexPolygons(pl);
		Span<Point> points = contiguousMultiPolygonCollection.Points.Span;
		for (int i = 0; i < contiguousMultiPolygonCollection.PointCount; i++)
		{
			points[i] = transform.Transform(points[i]);
		}
	}

	private void RedrawVector(VectorID id, Point point)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			RedrawPointVector(id, point);
			break;
		case VectorType.Segment:
			RedrawSegmentVector(id, point);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	private void RedrawVector(VectorID id, IReadOnlyList<Point> points)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			RedrawPointVector<IReadOnlyList<Point>>(id, points);
			break;
		case VectorType.Segment:
			RedrawSegmentVector(id, points);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	private void RedrawVector(VectorID id, Segment seg)
	{
		if (_vectorData[id].Type == VectorType.Segment)
		{
			RedrawSegmentVector(id, seg);
			return;
		}
		throw new NotImplementedException();
	}

	private void RedrawVector(VectorID id, IReadOnlyList<Segment> segments)
	{
		if (_vectorData[id].Type == VectorType.Segment)
		{
			RedrawSegmentVector<IReadOnlyList<Segment>>(id, segments);
			return;
		}
		throw new NotImplementedException();
	}

	private void RedrawVector(VectorID id, Polyline pl)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			RedrawPointVector(id, pl);
			break;
		case VectorType.Segment:
			RedrawSegmentVector(id, pl);
			break;
		case VectorType.Line:
			RedrawLineVector(id, pl);
			break;
		case VectorType.Polygon:
			throw new NotSupportedException();
		default:
			throw new NotImplementedException();
		}
	}

	private void RedrawVector(VectorID id, IReadOnlyList<Polyline> lines)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			RedrawPointVector(id, lines);
			break;
		case VectorType.Segment:
			RedrawSegmentVector(id, lines);
			break;
		case VectorType.Line:
			RedrawLineVector(id, lines);
			break;
		case VectorType.Polygon:
			throw new NotSupportedException();
		default:
			throw new NotImplementedException();
		}
	}

	private void RedrawVector(VectorID id, Polygon polygon)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			RedrawPointVector(id, polygon);
			break;
		case VectorType.Segment:
			RedrawSegmentVector(id, polygon);
			break;
		case VectorType.Line:
			RedrawLineVector(id, polygon);
			break;
		case VectorType.Polygon:
			RedrawPolygonVector(id, polygon);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	private void RedrawVector(VectorID id, IReadOnlyList<Polygon> polygons)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			RedrawPointVector(id, polygons);
			break;
		case VectorType.Segment:
			RedrawSegmentVector(id, polygons);
			break;
		case VectorType.Line:
			RedrawLineVector(id, polygons);
			break;
		case VectorType.Polygon:
			RedrawPolygonVector(id, polygons);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	private void RedrawVector(VectorID id, ComplexPolygon polygon)
	{
		if (_vectorData[id].Type == VectorType.ComplexPolygon)
		{
			RedrawComplexPolygonVector(id, polygon);
			return;
		}
		throw new NotImplementedException();
	}

	private void RedrawVector(VectorID id, IReadOnlyList<ComplexPolygon> polygons)
	{
		if (_vectorData[id].Type == VectorType.ComplexPolygon)
		{
			RedrawComplexPolygonVector(id, polygons);
			return;
		}
		throw new NotImplementedException();
	}

	private void RedrawVector(VectorID id, IReadOnlyList<ImmutableMultiPartPolygon> polygons)
	{
		if (_vectorData[id].Type == VectorType.ComplexPolygon)
		{
			RedrawComplexPolygonVector(id, polygons);
			return;
		}
		throw new NotImplementedException();
	}

	private void RedrawVector(VectorID id, IReadOnlyList<Memory<Point>> lines)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Line:
			RedrawLineVector(id, lines);
			break;
		case VectorType.Polygon:
			RedrawPolygonVector(id, lines);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	private void RedrawVector(VectorID id, Memory<Point> lines)
	{
		switch (_vectorData[id].Type)
		{
		case VectorType.Point:
			RedrawPointVector(id, lines);
			break;
		case VectorType.Polygon:
			RedrawPointVector(id, lines);
			break;
		case VectorType.Line:
			RedrawLineVector(id, lines);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	private void InternalRedrawVector<T>(VectorID id, IReadOnlyList<T> vectors, Func<Shape> InitializeShapeFunc, Action<Shape, T, Matrix3x3D> UpdateShapeFunc, Func<T, Extent, int> GetNumberOfRequiredShapes)
	{
		InternalRedrawVector(id, vectors, InitializeShapeFunc, (Memory<Shape> shapeMem, T t, Action<Shape, T, Matrix3x3D> payload, Matrix3x3D transform) =>
		{
			payload(shapeMem.Span[0], t, transform);
		}, UpdateShapeFunc, GetNumberOfRequiredShapes);
	}

	private void InternalRedrawVector<T>(VectorID id, T vector, Func<Shape> InitializeShapeFunc, Action<Shape, T, Matrix3x3D> UpdateShapeFunc, Func<T, Extent, int> GetNumberOfRequiredShapes)
	{
		InternalRedrawVector(id, vector, InitializeShapeFunc, (Memory<Shape> shapeMem, T t, Action<Shape, T, Matrix3x3D> payload, Matrix3x3D transform) =>
		{
			payload(shapeMem.Span[0], t, transform);
		}, UpdateShapeFunc, GetNumberOfRequiredShapes);
	}

	private void InternalRedrawVector<T, K>(VectorID id, IReadOnlyList<T> vectors, Func<Shape> InitializeShapeFunc, Action<Shape, T, K, Matrix3x3D> UpdateShapeFunc, K updateShapeFuncPayload, Func<T, Extent, int> GetNumberOfRequiredShapes)
	{
		InternalRedrawVector(id, vectors, InitializeShapeFunc, (Memory<Shape> shapeMem, T t, (Action<Shape, T, K, Matrix3x3D>, K) payload, Matrix3x3D transform) =>
		{
			payload.Item1(shapeMem.Span[0], t, payload.Item2, transform);
		}, (UpdateShapeFunc, updateShapeFuncPayload), GetNumberOfRequiredShapes);
	}

	private void InternalRedrawVector<T, K>(VectorID id, T vector, Func<Shape> InitializeShapeFunc, Action<Shape, T, K, Matrix3x3D> UpdateShapeFunc, K updateShapeFuncPayload, Func<T, Extent, int> GetNumberOfRequiredShapes)
	{
		InternalRedrawVector(id, vector, InitializeShapeFunc, (Memory<Shape> shapeMem, T t, (Action<Shape, T, K, Matrix3x3D>, K) payload, Matrix3x3D transform) =>
		{
			payload.Item1(shapeMem.Span[0], t, payload.Item2, transform);
		}, (UpdateShapeFunc, updateShapeFuncPayload), GetNumberOfRequiredShapes);
	}

	private void InternalRedrawVector<T>(VectorID id, IReadOnlyList<T> vectors, Func<Shape> InitializeShapeFunc, Action<Memory<Shape>, T, Matrix3x3D> UpdateShapeFunc, Func<T, Extent, int> GetNumberOfRequiredShapes)
	{
		VectorIDData<Shape, VectorGroup> data = _vectorData[id];
		InternalRedrawVector(data, vectors.Count, InitializeShapeFunc, (Memory<Shape> shapeMem, int index, (Action<Memory<Shape>, T, Matrix3x3D>, IReadOnlyList<T>) payload, Matrix3x3D transform) =>
		{
			payload.Item1(shapeMem, payload.Item2[index], transform);
		}, (UpdateShapeFunc, vectors), (int index, Extent screen, (Func<T, Extent, int>, IReadOnlyList<T>) payload) => payload.Item1(payload.Item2[index], screen), (GetNumberOfRequiredShapes, vectors));
	}

	private void InternalRedrawVector<T>(VectorID id, T vector, Func<Shape> InitializeShapeFunc, Action<Memory<Shape>, T, Matrix3x3D> UpdateShapeFunc, Func<T, Extent, int> GetNumberOfRequiredShapes)
	{
		VectorIDData<Shape, VectorGroup> data = _vectorData[id];
		InternalRedrawVector(data, 1, InitializeShapeFunc, (Memory<Shape> shapeMem, int index, (Action<Memory<Shape>, T, Matrix3x3D>, T) payload, Matrix3x3D transform) =>
		{
			payload.Item1(shapeMem, payload.Item2, transform);
		}, (UpdateShapeFunc, vector), (int index, Extent screen, (Func<T, Extent, int>, T) payload) => payload.Item1(payload.Item2, screen), (GetNumberOfRequiredShapes, vector));
	}

	private void InternalRedrawVector<T, K>(VectorID id, IReadOnlyList<T> vectors, Func<Shape> InitializeShapeFunc, Action<Memory<Shape>, T, K, Matrix3x3D> UpdateShapeFunc, K updateShapeFuncPayload, Func<T, Extent, int> GetNumberOfRequiredShapes)
	{
		VectorIDData<Shape, VectorGroup> data = _vectorData[id];
		InternalRedrawVector(data, vectors.Count, InitializeShapeFunc, (Memory<Shape> shapeMem, int index, (Action<Memory<Shape>, T, K, Matrix3x3D>, IReadOnlyList<T>, K) payload, Matrix3x3D transform) =>
		{
			payload.Item1(shapeMem, payload.Item2[index], payload.Item3, transform);
		}, (UpdateShapeFunc, vectors, updateShapeFuncPayload), (int index, Extent screen, (Func<T, Extent, int>, IReadOnlyList<T>) payload) => payload.Item1(payload.Item2[index], screen), (GetNumberOfRequiredShapes, vectors));
	}

	private void InternalRedrawVector<T, K>(VectorID id, T vector, Func<Shape> InitializeShapeFunc, Action<Memory<Shape>, T, K, Matrix3x3D> UpdateShapeFunc, K updateShapeFuncPayload, Func<T, Extent, int> GetNumberOfRequiredShapes)
	{
		VectorIDData<Shape, VectorGroup> data = _vectorData[id];
		InternalRedrawVector(data, 1, InitializeShapeFunc, (Memory<Shape> shapeMem, int index, (Action<Memory<Shape>, T, K, Matrix3x3D>, T, K) payload, Matrix3x3D transform) =>
		{
			payload.Item1(shapeMem, payload.Item2, payload.Item3, transform);
		}, (UpdateShapeFunc, vector, updateShapeFuncPayload), (int index, Extent screen, (Func<T, Extent, int>, T) payload) => payload.Item1(payload.Item2, screen), (GetNumberOfRequiredShapes, vector));
	}

	private void InternalRedrawVector<K, L>(VectorIDData<Shape, VectorGroup> data, int totalVectors, Func<Shape> initializeShapeFunc, Action<Memory<Shape>, int, K, Matrix3x3D> updateShapeFunc, K updateShapeFuncPayload, Func<int, Extent, L, int> getNumberOfRequiredShapes, L getNumberOfRequiredShapesPayload)
	{
		if (data.QueuedForDelete)
		{
			throw new InvalidOperationException("Redrawing deleted vector.");
		}
		data.IncrementRenderState(VectorActionType.Redraw);
		long renderState = data.RenderState;
		RedrawVectorDataPackage<K, L> redrawVectorDataPackage = RedrawVectorDataPackage<K, L>.Pool.Acquire();
		redrawVectorDataPackage.VectorManager = this;
		redrawVectorDataPackage.CachedRenderState = renderState;
		redrawVectorDataPackage.Data = data;
		redrawVectorDataPackage.TotalVectors = totalVectors;
		redrawVectorDataPackage.GetNumberOfRequiredShapes = getNumberOfRequiredShapes;
		redrawVectorDataPackage.InitializeShapeFunc = initializeShapeFunc;
		redrawVectorDataPackage.GetNumbRequiredShapesModel = getNumberOfRequiredShapesPayload;
		redrawVectorDataPackage.UpdateShapeModel = updateShapeFuncPayload;
		redrawVectorDataPackage.UpdateShapeFunc = updateShapeFunc;
		redrawVectorDataPackage.Transform = BaseTransform;
		QueueChange((object model) =>
		{
			RedrawVectorDataPackage<K, L> model2 = (model as RedrawVectorDataPackage<K, L>) ?? throw new InvalidOperationException();
			VectorManager<VectorCanvas, Shape, ContiguousPolygonCollection, ContiguousMultiPolygonCollection, SegmentShape, ContiguousLineCollection, ContiguousEllipseCollection, TextShape, VectorGroup> vectorManager = model2.VectorManager;
			long cachedRenderState = model2.CachedRenderState;
			VectorIDData<Shape, VectorGroup> data2 = model2.Data;
			int totalVectors2 = model2.TotalVectors;
			Func<int, Extent, L, int> getNumberOfRequiredShapes2 = model2.GetNumberOfRequiredShapes;
			Func<Shape> initializeShapeFunc2 = model2.InitializeShapeFunc;
			L getNumbRequiredShapesModel = model2.GetNumbRequiredShapesModel;
			K updateShapeModel = model2.UpdateShapeModel;
			Action<Memory<Shape>, int, K, Matrix3x3D> updateShapeFunc2 = model2.UpdateShapeFunc;
			Matrix3x3D transform = model2.Transform;
			model2.Clear();
			if (cachedRenderState == data2.RenderState)
			{
				vectorManager.ValidateVectorMapContains(data2.ID);
				Extent clipExtent = vectorManager.GetClipExtent();
				Memory<Shape> currentlyUsedShapeCache = vectorManager.UpdateShapeCache(data2, totalVectors2, initializeShapeFunc2);
				int getNumberOfRequiredShapes2Sum = 0;
				for (int i = 0; i < totalVectors2; i++)
				{
					int getNumberOfRequiredShapes22 = getNumberOfRequiredShapes2(i, clipExtent, getNumbRequiredShapesModel);
					if (getNumberOfRequiredShapes22 != 0)
					{
						updateShapeFunc2?.Invoke(currentlyUsedShapeCache.Slice(getNumberOfRequiredShapes2Sum, getNumberOfRequiredShapes22), i, updateShapeModel, transform);
						getNumberOfRequiredShapes2Sum += getNumberOfRequiredShapes22;
					}
				}
				data2.CurrentlyUsedShapeCache = currentlyUsedShapeCache;
			}
		}, redrawVectorDataPackage);
	}

	private Memory<Shape> UpdateShapeCache(VectorIDData<Shape, VectorGroup> data, int newShapeCount, Func<Shape> InitializeShapeFunc)
	{
		if (newShapeCount == data.CurrentlyUsedShapeCache.Length)
		{
			return data.CurrentlyUsedShapeCache;
		}
		int length = data.CurrentlyUsedShapeCache.Length;
		int newShapeCountAboveLength = newShapeCount - length;
		if (data.OriginalShapeCache == null || newShapeCount > data.OriginalShapeCache.Length)
		{
			int count = Math.Max(newShapeCount, length * 2);
			int unity;
			if (_shapeArrayCache.Count <= 0)
			{
				unity = -1;
			}
			else
			{
				IList<int> keys = _shapeArrayCache.Keys;
				unity = keys[keys.Count - 1];
			}
			int ordinal = unity;
			Shape[] originalShapeCache;
			if (ordinal >= count)
			{
				originalShapeCache = _shapeArrayCache[ordinal];
				_shapeArrayCache.Remove(ordinal);
			}
			else
			{
				originalShapeCache = new Shape[count];
			}
			if (data.OriginalShapeCache != null)
			{
				Array.Copy(data.OriginalShapeCache, originalShapeCache, length);
				if (_shapeArrayCache.Count != _shapeArrayCacheMaxSize || data.OriginalShapeCache.Length > _shapeArrayCache.First().Key)
				{
					_shapeArrayCache[data.OriginalShapeCache.Length] = data.OriginalShapeCache;
				}
				while (_shapeArrayCache.Count > _shapeArrayCacheMaxSize)
				{
					_shapeArrayCache.RemoveAt(0);
				}
			}
			data.OriginalShapeCache = originalShapeCache;
		}
		Memory<Shape> result = data.OriginalShapeCache.AsMemory().Slice(0, newShapeCount);
		Span<Shape> shapes = result.Span;
		while (newShapeCountAboveLength > 0)
		{
			Shape initializeShapeFunc = InitializeShapeFunc();
			shapes[result.Length - newShapeCountAboveLength] = initializeShapeFunc;
			newShapeCountAboveLength--;
		}
		int newShapeCountAboveLength2 = newShapeCount - length;
		data.Group.SetCapacity(newShapeCount);
		while (newShapeCountAboveLength2 > 0)
		{
			Shape shape = shapes[result.Length - newShapeCountAboveLength2];
			AddShapeToScreen(data, shape);
			newShapeCountAboveLength2--;
		}
		if (newShapeCountAboveLength2 < 0)
		{
			for (int i = 0; i < -newShapeCountAboveLength2; i++)
			{
				ReturnShapeToPoolAndDisposeResources(data.Type, data.OriginalShapeCache[newShapeCount + i]);
			}
			BatchRemoveEndShapesFromScreen(data, -newShapeCountAboveLength2);
		}
		return result;
	}

	private void UpdateSymbology(VectorIDData<Shape, VectorGroup> data, VectorSymbology symb)
	{
		if (data.QueuedForDelete)
		{
			throw new InvalidOperationException("Updating symbology of deleted vector.");
		}
		if (symb == null)
		{
			return;
		}
		bool hasQueueSymbology = data.QueueSymbology != null;
		data.QueueSymbology = symb;
		if (hasQueueSymbology)
		{
			return;
		}
		IVectorCanvas vectorCanvas = data.Group;
		if (vectorCanvas.Fill == symb.FillColor && vectorCanvas.Stroke == symb.PenColor && vectorCanvas.StrokeThickness == symb.PenWidth * VectorManagerOptions.RenderScaling && vectorCanvas.IsStrokeDotted == symb.IsDotted && vectorCanvas.DottedStrokeStyle == symb.DottedStrokeStyle && vectorCanvas.Translation == symb.Translation && vectorCanvas.IsTranslated == symb.IsTranslated && vectorCanvas.HaloColor == symb.HaloColor && vectorCanvas.HaloThickness == symb.HaloThickness && vectorCanvas.Font == symb.Font && vectorCanvas.TextEmphasis == symb.TextEmphasis && vectorCanvas.TextMaxWidth == symb.TextMaxWidth && vectorCanvas.TextKerning == symb.TextKerning)
		{
			data.QueueSymbology = null;
			return;
		}
		UpdateSymbologyDataPackage updateSymbologyDataPackage = UpdateSymbologyDataPackage.Pool.Acquire();
		updateSymbologyDataPackage.VectorManager = this;
		updateSymbologyDataPackage.Data = data;
		QueueChange((object model) =>
		{
			if (!(model is UpdateSymbologyDataPackage updateSymbologyDataPackage2))
			{
				throw new ArgumentException();
			}
			_ = updateSymbologyDataPackage2.VectorManager;
			VectorIDData<Shape, VectorGroup> data2 = updateSymbologyDataPackage2.Data;
			VectorSymbology queueSymbology = data2.QueueSymbology;
			updateSymbologyDataPackage2.Clear();
			if (!data2.IsDisposed)
			{
				if (queueSymbology != null)
				{
					data2.Symbology = queueSymbology;
					VectorGroup vectorGroup = data2.Group;
					vectorGroup.Fill = queueSymbology.FillColor;
					vectorGroup.Stroke = queueSymbology.PenColor;
					vectorGroup.StrokeThickness = queueSymbology.PenWidth * VectorManagerOptions.RenderScaling;
					vectorGroup.IsStrokeDotted = queueSymbology.IsDotted;
					vectorGroup.DottedStrokeStyle = queueSymbology.DottedStrokeStyle;
					vectorGroup.Translation = queueSymbology.Translation;
					vectorGroup.IsTranslated = queueSymbology.IsTranslated;
					vectorGroup.HaloColor = queueSymbology.HaloColor;
					vectorGroup.HaloThickness = queueSymbology.HaloThickness;
					vectorGroup.Font = queueSymbology.Font;
					vectorGroup.TextEmphasis = queueSymbology.TextEmphasis;
					vectorGroup.TextMaxWidth = queueSymbology.TextMaxWidth;
					vectorGroup.TextKerning = queueSymbology.TextKerning;
				}
				data2.QueueSymbology = null;
			}
		}, updateSymbologyDataPackage);
	}

	public void DeleteVector(VectorID id)
	{
		VectorIDData<Shape, VectorGroup> vectorIDData = _vectorData[id];
		if (vectorIDData.QueuedForDelete)
		{
			throw new InvalidOperationException("Clearing deleted vector.");
		}
		vectorIDData.QueueForDelete();
		DeleteVectorDataPackage deleteVectorDataPackage = DeleteVectorDataPackage.Pool.Acquire();
		deleteVectorDataPackage.VectorManager = this;
		deleteVectorDataPackage.Data = vectorIDData;
		deleteVectorDataPackage.ID = id;
		deleteVectorDataPackage.CachedRenderState = vectorIDData.RenderState;
		QueueChange((object model) =>
		{
			if (!(model is DeleteVectorDataPackage { VectorManager: var vectorManager, Data: var data, ID: var iD } deleteVectorDataPackage2))
			{
				throw new ArgumentException();
			}
			deleteVectorDataPackage2.Clear();
			if (data.RenderState != deleteVectorDataPackage2.CachedRenderState || data.IsDisposed)
			{
				throw new InvalidOperationException("Failed to delete a vector because it has changed after deleting.");
			}
			vectorManager.DeleteVector_NoQueue(data, iD);
		}, deleteVectorDataPackage);
	}

	private void DeleteVector_NoQueue(VectorIDData<Shape, VectorGroup> data, VectorID id)
	{
		ValidateVectorMapContains(id);
		Canvas.RemoveChild(data.Group);
		_vectorData.Remove(id);
		data.QueueSymbology = null;
		data.Symbology = null;
		data.IsDisposed = true;
		DeleteVector_CleanupResources(data, id);
	}

	private void DeleteVector_CleanupResources(VectorIDData<Shape, VectorGroup> data, VectorID id)
	{
		if (VectorManagerOptions.USE_VECTOR_POOLING)
		{
			data.Group.DisposeInternalResources();
			data.Group.Clear();
			_vectorGroupPool.Return(data.Group);
		}
		else
		{
			data.Group.Dispose();
		}
		if (data.OriginalShapeCache != null)
		{
			for (int i = 0; i < data.CurrentlyUsedShapeCache.Length; i++)
			{
				Shape shape = data.OriginalShapeCache[i];
				ReturnShapeToPoolAndDisposeResources(data.Type, shape);
			}
			_shapeArrayCache[data.OriginalShapeCache.Length] = data.OriginalShapeCache;
			data.OriginalShapeCache = null;
			data.CurrentlyUsedShapeCache = Memory<Shape>.Empty;
		}
	}

	private void ReturnShapeToPoolAndDisposeResources(VectorType type, Shape shape)
	{
		if (shape is IDisposable disposable)
		{
			disposable.Dispose();
		}
		if (VectorManagerOptions.USE_VECTOR_POOLING)
		{
			switch (type)
			{
			case VectorType.Point:
				_ellipseCollectionPool.Return(shape as ContiguousEllipseCollection);
				break;
			case VectorType.Segment:
				_segmentPool.Return(shape as SegmentShape);
				break;
			case VectorType.Line:
				_lineCollectionPool.Return(shape as ContiguousLineCollection);
				break;
			case VectorType.Polygon:
				_polygonCollectionPool.Return(shape as ContiguousPolygonCollection);
				break;
			case VectorType.Text:
				_textPool.Return(shape as TextShape);
				break;
			case VectorType.ComplexPolygon:
				_complexPolygonCollectionPool.Return(shape as ContiguousMultiPolygonCollection);
				break;
			default:
				throw new NotImplementedException();
			}
		}
	}

	private void BatchRemoveEndShapesFromScreen(VectorIDData<Shape, VectorGroup> data, int count)
	{
		data.Group.TrimShapesFromEnd(count);
		_onscreenShapeCount -= count;
	}

	public void TranslateAllVectorsDelta(double deltaXWorldSpace, double deltaYWorldSpace)
	{
		Point point = new Point(deltaXWorldSpace, deltaYWorldSpace);
		TransformationFromBase = TransformationFromBase.TranslateScaleSensitiveSafe(point.X, 0.0 - point.Y);
	}

	public void Zoom(Point centerOnScreenPixel, double scale)
	{
		TransformationFromBase = TransformationFromBase.ScaleSafe(scale, centerOnScreenPixel.X, centerOnScreenPixel.Y);
	}

	public void ResetAllVectorTranslation()
	{
		TransformationFromBase = TransformationFromBase.ResetTranslationSafe();
	}

	public void ResetZoom()
	{
	}

	public void SetVectorZIndex(VectorID id, int zIdx)
	{
		if ((long)id != 0L)
		{
			VectorIDData<Shape, VectorGroup> data = _vectorData[id];
			SetVectorZIndexDataPackage setVectorZIndexDataPackage = SetVectorZIndexDataPackage.Pool.Acquire();
			setVectorZIndexDataPackage.Data = data;
			setVectorZIndexDataPackage.ZIndex = zIdx;
			setVectorZIndexDataPackage.VectorManager = this;
			QueueChange((object model) =>
			{
				SetVectorZIndexDataPackage model2 = (model as SetVectorZIndexDataPackage) ?? throw new InvalidOperationException();
				VectorIDData<Shape, VectorGroup> data2 = model2.Data;
				int zIndex = model2.ZIndex;
				VectorManager<VectorCanvas, Shape, ContiguousPolygonCollection, ContiguousMultiPolygonCollection, SegmentShape, ContiguousLineCollection, ContiguousEllipseCollection, TextShape, VectorGroup> vectorManager = model2.VectorManager;
				model2.Clear();
				data2.ZIndex = zIndex;
				vectorManager._canvas.SetZIndex(data2.Group, zIndex);
			}, setVectorZIndexDataPackage);
		}
	}

	public void TrySetVectorZIndex(VectorID id, int zIdx)
	{
		if ((long)id != 0L)
		{
			SetVectorZIndex(id, zIdx);
		}
	}

	private int GetOnScreenTexts(ILabelDefinition def, Extent screen)
	{
		return 1;
	}

	private int GetOnScreenPoints(Point pt, Extent screen)
	{
		return 1;
	}

	private int GetOnScreenPoints(double x, double y, Extent screen)
	{
		return screen.Contains(x, y) ? 1 : 0;
	}

	private int GetOnScreenPoints<T>(T pts, Extent screen) where T : IReadOnlyList<Point>
	{
		return pts.Count;
	}

	private int GetOnScreenPoints(Segment seg, Extent screen)
	{
		return GetOnScreenPoints(seg.A, screen) + GetOnScreenPoints(seg.B, screen);
	}

	private int GetOnScreenPoints(IReadOnlyList<Segment> segments, Extent screen)
	{
		throw new NotImplementedException();
	}

	private int GetOnScreenPoints(IReadOnlyList<double> xs, IReadOnlyList<double> ys, Extent screen)
	{
		int onScreenPoints = 0;
		for (int i = 0; i < xs.Count; i++)
		{
			if (screen.Contains(xs[i], ys[i]))
			{
				onScreenPoints++;
			}
		}
		return onScreenPoints;
	}

	private static int GetOnScreenLines(IReadOnlyList<Segment> segs, Extent screen)
	{
		int onScreenLines = 0;
		foreach (Segment seg in segs)
		{
			if (seg.ClipToExtent(screen))
			{
				onScreenLines++;
				break;
			}
		}
		return onScreenLines;
	}

	private static int GetOnScreenLines(IReadOnlyList<IReadOnlyList<Segment>> segsList, Extent screen)
	{
		int onScreenLines = 0;
		foreach (IReadOnlyList<Segment> segs in segsList)
		{
			foreach (Segment seg in segs)
			{
				if (seg.ClipToExtent(screen))
				{
					onScreenLines++;
					break;
				}
			}
		}
		return onScreenLines;
	}

	private int GetOnScreenLines<T>(T t, Extent extent) where T : IReadOnlyList<Point>
	{
		return 1;
	}

	private static int GetOnScreenLines<T>(IReadOnlyList<T> ts, Extent screen) where T : IReadOnlyList<Point>
	{
		return 1;
	}

	private int GetOnScreenPolygons<T>(T t, Extent screen) where T : IReadOnlyList<Point>
	{
		return 1;
	}

	private int GetOnScreenSegments(Point pt, Extent screen)
	{
		return 0;
	}

	private int GetOnScreenSegments(Segment seg, Extent screen)
	{
		return seg.ClipToExtent(screen) ? 1 : 0;
	}

	private int GetOnScreenSegments(IReadOnlyList<Segment> segments, Extent screen)
	{
		int onScreenSegments = 0;
		foreach (Segment segment in segments)
		{
			if (segment.ClipToExtent(screen))
			{
				onScreenSegments++;
			}
		}
		return onScreenSegments;
	}

	private int GetOnScreenSegments<T>(T line, Extent screen) where T : Polyline
	{
		return GetOnScreenSegments(line.Segments, screen);
	}

	private int GetOnScreenSegments<T>(IReadOnlyList<T> lines, Extent screen) where T : IReadOnlyList<Point>
	{
		throw new NotImplementedException();
	}

	private void ClearCachedData(VectorID id)
	{
		_vectorData[id].DataCache = null;
	}

	private void UpdateCachedData<T>(VectorID id, T t)
	{
		_vectorData[id].DataCache = t;
	}

	private void UpdateCachedData<T>(VectorID id, IReadOnlyList<T> ts)
	{
		_vectorData[id].DataCache = ts;
	}

	private void UpdateCachedData<T>(VectorID id, IReadOnlyList<T> xs, IReadOnlyList<T> ys)
	{
		_vectorData[id].DataCache = (xs, ys);
	}

	public void RedrawAllFromCachedData(VectorManagerRedrawReason reason = VectorManagerRedrawReason.Unknown)
	{
		foreach (VectorIDData<Shape, VectorGroup> vectorIDData in _vectorData.Values)
		{
			if (!vectorIDData.QueuedForDelete)
			{
				RedrawFromCachedData(vectorIDData);
			}
		}
	}

	public void RedrawFromCachedData(VectorIDData<Shape, VectorGroup> data)
	{
		VectorID id = data.ID;
		object dataCache = data.DataCache;
		if (dataCache == null)
		{
			TryClearVector(ref id);
		}
		else if (!(dataCache is IReadOnlyList<Polygon> polygons))
		{
			if (!(dataCache is Polygon polygon))
			{
				if (!(dataCache is ComplexPolygon polygon2))
				{
					if (!(dataCache is IReadOnlyList<ComplexPolygon> polygons2))
					{
						if (!(dataCache is IReadOnlyList<ImmutableMultiPartPolygon> polygons3))
						{
							if (!(dataCache is TriangulatedPolygonData data2))
							{
								if (!(dataCache is IReadOnlyList<Polyline> lines))
								{
									if (!(dataCache is Polyline pl))
									{
										if (!(dataCache is IReadOnlyList<Segment> segments))
										{
											if (!(dataCache is Segment seg))
											{
												if (!(dataCache is IReadOnlyList<Point> points))
												{
													if (!(dataCache is Point point))
													{
														if (!(dataCache is ILabelDefinition iLabelDefinition))
														{
															if (!(dataCache is IReadOnlyList<ILabelDefinition> iReadOnlyList))
															{
																if (!(dataCache is IReadOnlyList<Memory<Point>> lines2))
																{
																	if (!(dataCache is Memory<Point> lines3))
																	{
																		throw new NotImplementedException();
																	}
																	RedrawVector(id, lines3);
																}
																else
																{
																	RedrawVector(id, lines2);
																}
															}
															else
															{
																RedrawTextVector(id, iReadOnlyList);
															}
														}
														else
														{
															RedrawTextVector(id, iLabelDefinition);
														}
													}
													else
													{
														RedrawVector(id, point);
													}
												}
												else
												{
													RedrawVector(id, points);
												}
											}
											else
											{
												RedrawVector(id, seg);
											}
										}
										else
										{
											RedrawVector(id, segments);
										}
									}
									else
									{
										RedrawVector(id, pl);
									}
								}
								else
								{
									RedrawVector(id, lines);
								}
							}
							else
							{
								RedrawPreTriangulatedPolygonVector(id, data2);
							}
						}
						else
						{
							RedrawVector(id, polygons3);
						}
					}
					else
					{
						RedrawVector(id, polygons2);
					}
				}
				else
				{
					RedrawVector(id, polygon2);
				}
			}
			else
			{
				RedrawVector(id, polygon);
			}
		}
		else
		{
			RedrawVector(id, polygons);
		}
	}

	public PolylineVectorAnimation TrackAnimation(Polyline pl)
	{
		return new PolylineVectorAnimation(this, pl);
	}

	public CulvertFlowAnimation TrackCulvertAnimation(Polyline pl)
	{
		return new CulvertFlowAnimation(this, pl);
	}

	private void ValidateVectorMapContains(VectorID id)
	{
		if (!_vectorData.ContainsKey(id))
		{
			throw new ArgumentException("This vector is not currently being tracked.");
		}
	}

	private Extent GetClipExtent()
	{
		return _getVisibleWorldExtent().RatioAroundCenter(2.0);
	}

	private void ParseVectors(IList<object> vectors, out List<Point> points, out List<Segment> segments, out List<Polyline> polylines, out List<Polygon> polygons, out List<ComplexPolygon> complexPolygons)
	{
		points = null;
		segments = null;
		polygons = null;
		polylines = null;
		complexPolygons = null;
		if (vectors == null)
		{
			return;
		}
		foreach (object vector in vectors)
		{
			if (vector == null)
			{
				continue;
			}
			if (!(vector is Polygon polygon))
			{
				if (!(vector is Polyline polyline))
				{
					if (!(vector is Segment segment))
					{
						if (!(vector is Point point))
						{
							if (!(vector is Mesh mesh))
							{
								if (!(vector is ComplexPolygon complexPolygon))
								{
									throw new NotImplementedException(vector.GetType().ToString() + " is an unknown vector type to render.");
								}
								if (complexPolygons == null)
								{
									complexPolygons = new List<ComplexPolygon>();
								}
								complexPolygons.Add(complexPolygon);
							}
							else
							{
								if (polygons == null)
								{
									polygons = new List<Polygon>();
								}
								polygons.Add(mesh.Perimeter);
							}
						}
						else
						{
							if (points == null)
							{
								points = new List<Point>();
							}
							points.Add(point);
						}
					}
					else
					{
						if (segments == null)
						{
							segments = new List<Segment>();
						}
						segments.Add(segment);
					}
				}
				else
				{
					if (polylines == null)
					{
						polylines = new List<Polyline>();
					}
					polylines.Add(polyline);
				}
			}
			else
			{
				if (polygons == null)
				{
					polygons = new List<Polygon>();
				}
				polygons.Add(polygon);
			}
		}
	}

	private void TryUpdateDynamicVectors()
	{
		if (_lastDynamicVectorUpdateExtent.IsNull() || _lastDynamicVectorUpdateTransformation.M11 != TransformationFromBase.M11 || _lastDynamicVectorUpdateTransformation.M21 != TransformationFromBase.M21)
		{
			RedrawAllDynamicVectors();
			return;
		}
		Extent extent = _getVisibleWorldExtent();
		double width = extent.Width;
		double height = extent.Height;
		Point upperLeft = extent.UpperLeft;
		Point upperLeft2 = _lastDynamicVectorUpdateExtent.UpperLeft;
		if (Math.Abs(upperLeft.X - upperLeft2.X) >= width / 2.0 || Math.Abs(upperLeft.Y - upperLeft2.Y) >= height / 2.0)
		{
			RedrawAllDynamicVectors();
		}
	}

	private void RedrawAllDynamicVectors()
	{
		if (_vectorData == null)
		{
			return;
		}
		_lastDynamicVectorUpdateTransformation = TransformationFromBase;
		_lastDynamicVectorUpdateExtent = _getVisibleWorldExtent();
		foreach (VectorIDData<Shape, VectorGroup> vectorIDData in _vectorData.Values)
		{
			if (vectorIDData.UsingDynamicDataSource)
			{
				RedrawFromCachedData(vectorIDData);
			}
		}
	}

	public List<IMetadataBase> GetMetadata()
	{
		return new List<IMetadataBase>
		{
			new NumericalMetaData("Tracked VectorIDs", _vectorData.Count),
			new NumericalMetaData("Rendered Shapes", _onscreenShapeCount)
		};
	}

	public void SetVectorDynamicDataSource(VectorID id, Func<object> dynamicDataFunc)
	{
		_vectorData[id].SetDynamicDataSource(dynamicDataFunc);
		ForceRedrawDynamicDataSourceVector(id);
		Canvas.Invalidate();
	}

	public void ForceRedrawDynamicDataSourceVector(VectorID id)
	{
		VectorIDData<Shape, VectorGroup> vectorIDData = _vectorData[id];
		if (vectorIDData.UsingDynamicDataSource)
		{
			RedrawFromCachedData(vectorIDData);
			return;
		}
		throw new InvalidOperationException("This vector does not have a dynamic data source.");
	}

	public void SetVectorClipExtent(VectorID id, Memory<Point> extent)
	{
		Span<Point> extentSpan = extent.Span;
		VectorGroup vectorGroup = _vectorData[id].Group;
		Memory<Point> clipExtent = vectorGroup.ClipExtent;
		if (!clipExtent.IsEmpty)
		{
			clipExtent = vectorGroup.ClipExtent;
			if (clipExtent.Length == extent.Length)
			{
				goto IL_0067;
			}
		}
		vectorGroup.ClipExtent = new Point[extent.Length];
		goto IL_0067;
		IL_0067:
		clipExtent = vectorGroup.ClipExtent;
		Span<Point> clipExtentSpan = clipExtent.Span;
		for (int i = 0; i < extent.Length; i++)
		{
			clipExtentSpan[i] = BaseTransform.Transform(extentSpan[i]);
		}
		vectorGroup.CompleteClip();
	}
}
