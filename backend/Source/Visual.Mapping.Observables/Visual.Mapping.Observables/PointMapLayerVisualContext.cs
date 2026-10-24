using System;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.Helpers;
using Geospatial.IO;
using Geospatial.IO.Vectors;
using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;
using Visual.Mapping.Observables.Map;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class PointMapLayerVisualContext : VectorFileMapLayerVisualContext<PointCollection, Point>, IPointVectorContext, IVectorContext<Point>, IVectorFeatureContext
{
	private const string POINT_SYMBOLOGY_ID = "Point_Map_Layer_Symbology";

	private const string POINT_SYMBOLOGY_HEADER = "Point";

	private const string POINT_MAP_LAYER_NAME = "Point Map Layer";

	private static readonly VectorSymbologyDefinition _defaultPointSymbDef = new VectorSymbologyDefinition("Point_Map_Layer_Symbology", "Point", Geospatial.Rendering.Systems.Vector.VectorType.Point, DefaultEditSymbology.PointMapLayerSymbology, "VectorSymbology", "Point Map Layer");

	private static readonly VectorSymbologyDefinition[] _defaultSymbologyDefinitions = new VectorSymbologyDefinition[1] { _defaultPointSymbDef };

	protected static readonly ILogger _logger = LogHelper.GetLogger("PointMapLayerVisualContext");

	public const string PointsXmlNodeIdentifier = "PointsVector";

	private PointCollection _dataLayer;

	private IPointCollection _editCollection;

	public static VectorSymbologyDefinition[] DefaultSymbologyDefinitions => _defaultSymbologyDefinitions;

	protected override VectorSymbologyDefinition[] SymbologyDefinitions => _defaultSymbologyDefinitions;

	public static Type Type => typeof(PointMapLayerVisualContext);

	public override string XmlNodeIdentifier => "PointsVector";

	public override string DefaultName => "Point Layer";

	public override PointRenderLayer RenderLayer => base.RenderLayer as PointRenderLayer;

	public PointCollection Points => _dataLayer;

	protected override PointCollection FeatureCollection => Points;

	IPointCollection IPointVectorContext.Points => _editCollection ?? Points;

	public PointMapLayerVisualContext(string filename, Projection projection, MapBase map)
		: base(filename, projection, map)
	{
	}

	protected override OperationResult LoadFromFile(out PointCollection features, out ObservableTable table, out IRenderLayer renderLayer)
	{
		OperationResult result = RenderLayerFrom(Filename, _projection, out features, out table, out var renderLayer2);
		renderLayer = renderLayer2;
		return result;
	}

	protected override void StoreLoadedFeatures(PointCollection features, ObservableTable table, bool rowsMatchFeatures)
	{
		_dataLayer = features;
		IPointCollection editCollection;
		if (!rowsMatchFeatures)
		{
			IPointCollection pointCollection = features;
			editCollection = pointCollection;
		}
		else
		{
			IPointCollection pointCollection = new ObservablePointFeatureCollection(features, table);
			editCollection = pointCollection;
		}
		_editCollection = editCollection;
	}

	public static OperationResult RenderLayerFrom(string filename, Projection targetProjection, out PointCollection points, out ObservableTable table, out PointRenderLayer renderLayer)
	{
		return RenderLayerFrom(filename, filename, targetProjection, out points, out table, out renderLayer);
	}

	/// <summary>Reads source bytes from <paramref name="filename"/> while using <paramref name="formatHintPath"/> only to dispatch the retained format.</summary>
	/// <remarks>A distinct format hint must be validated by the caller against the exact persisted source row.</remarks>
	public static OperationResult RenderLayerFrom(string filename, string formatHintPath, Projection targetProjection, out PointCollection points, out ObservableTable table, out PointRenderLayer renderLayer)
	{
		points = null;
		table = null;
		renderLayer = null;
		OperationResult operationResult;
		if (filename.RoughlyEndsWith(".shp"))
		{
			operationResult = ShapefileIO.TryRead(filename, out PointFeatureCollection collection, targetProjection, (ProgressReporter)null);
			if (!operationResult.Result)
			{
				return operationResult;
			}
			points = collection.AsPointCollection();
			table = new ObservableTable(collection.AttributeTable);
		}
		else
		{
			if (!GeoJsonReader.HasJsonFileExtension(formatHintPath))
			{
				throw new NotImplementedException("Unsupported/unrecognized file format.");
			}
			operationResult = GeoJsonReader.TryRead(filename, out PointFeatureCollection collection2);
			if (!operationResult.Result)
			{
				return operationResult;
			}
			points = collection2.AsPointCollection();
			if (targetProjection != null)
			{
				using ReprojectionTransformer reprojectionTransformer = new ReprojectionTransformer(WellKnownProjections.WGS84, targetProjection);
				Memory<Point> pointBufferUnsafe = points.GetPointBufferUnsafe();
				reprojectionTransformer.Transform(pointBufferUnsafe, pointBufferUnsafe);
			}
			table = new ObservableTable(collection2.AttributeTable);
		}
		renderLayer = new PointRenderLayer(points);
		return operationResult;
	}

	protected override int GetFeatureHash(Point feature)
	{
		return feature.GetHashCode();
	}

	protected override OperationResult WriteShapefile()
	{
		return ShapefileIO.Write(Filename, _dataLayer, AttributeTable?.Table);
	}

	protected override OperationResult WriteGeoJson()
	{
		return GeoJsonWriter.Write(Filename, _dataLayer, _projection, AttributeTable?.Table);
	}

	public int HitTestClosestFeature(HitTestArgs args, int pixelCount = 8)
	{
		if (!HitTestHelpers.TryHitTestClosestFeature(args.QueryPoint, (double)pixelCount * args.Window.CellSize, _dataLayer.PointSpatialIndex, (Point point, int idx) => _dataLayer[idx].DistanceTo(point), out var nearestFeature))
		{
			return -1;
		}
		return nearestFeature;
	}
}
