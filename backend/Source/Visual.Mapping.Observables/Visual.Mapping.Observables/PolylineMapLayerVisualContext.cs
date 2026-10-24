using System;
using System.Collections.Generic;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.IO.Vectors;
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

public class PolylineMapLayerVisualContext : VectorFileMapLayerVisualContext<IPolylineCollection, Polyline>, IPolylineVectorContext, IVectorContext<Polyline>, IVectorFeatureContext
{
	private const string LINE_SYMBOLOGY_ID = "Polyline_Line_Symbology";

	private const string VERTEX_SYMBOLOGY_ID = "Polyline_Vertex_Symbology";

	private const string LINE_SYMBOLOGY_HEADER = "Polyline";

	private const string VERTEX_SYMBOLOGY_HEADER = "Vertex";

	private const string POLYLINE_MAP_LAYER_NAME = "Polyline Map Layer";

	private static readonly VectorSymbologyDefinition _defaultLineSymbDef = new VectorSymbologyDefinition("Polyline_Line_Symbology", "Polyline", Geospatial.Rendering.Systems.Vector.VectorType.Line, DefaultEditSymbology.PolylineMapLayerLineSymbology, "VectorSymbology", "Polyline Map Layer");

	private static readonly VectorSymbologyDefinition _defaultVertexSymbDef = new VectorSymbologyDefinition("Polyline_Vertex_Symbology", "Vertex", Geospatial.Rendering.Systems.Vector.VectorType.Point, DefaultEditSymbology.PolylineMapLayerVertexSymbology, "VertexSymbology", "Polyline Map Layer");

	private static readonly VectorSymbologyDefinition[] _defaultSymbologyDefinitions = new VectorSymbologyDefinition[2] { _defaultLineSymbDef, _defaultVertexSymbDef };

	private static readonly ILogger _logger = LogHelper.GetLogger("PolylineMapLayerVisualContext");

	public const string PolylinesXmlNodeIdentifier = "PolylinesVector";

	public static VectorSymbologyDefinition[] DefaultSymbologyDefinitions => _defaultSymbologyDefinitions;

	protected override VectorSymbologyDefinition[] SymbologyDefinitions => _defaultSymbologyDefinitions;

	public static Type Type => typeof(PolylineMapLayerVisualContext);

	public override string XmlNodeIdentifier => "PolylinesVector";

	public override string DefaultName => "Polyline Layer";

	public override PolylineRenderLayer RenderLayer => base.RenderLayer as PolylineRenderLayer;

	public IPolylineCollection Polylines { get; private set; }

	protected override IPolylineCollection FeatureCollection => Polylines;

	protected override string ToolTag => "PolylineMap";

	public PolylineMapLayerVisualContext(string filename, Projection projection, MapBase map)
		: base(filename, projection, map)
	{
	}

	protected override OperationResult LoadFromFile(out IPolylineCollection features, out ObservableTable table, out IRenderLayer renderLayer)
	{
		OperationResult result = RenderLayerFrom(Filename, _projection, out features, out table, out var renderLayer2);
		renderLayer = renderLayer2;
		return result;
	}

	protected override void StoreLoadedFeatures(IPolylineCollection features, ObservableTable table, bool rowsMatchFeatures)
	{
		IPolylineCollection polylines;
		if (!rowsMatchFeatures)
		{
			polylines = features;
		}
		else
		{
			IPolylineCollection polylineCollection = new ObservablePolylineFeatureCollection(features, table);
			polylines = polylineCollection;
		}
		Polylines = polylines;
	}

	protected override IReadOnlyList<ToolNode> BuildAdditionalTools()
	{
		return StandardToolFactories.PolylineGeoprocessingTools(this, Polylines, SelectedFeatureIDs, "PolylineMap", null, null, DeepConnectSupport.MakeSupplier(Map, Polylines));
	}

	public static OperationResult RenderLayerFrom(string filename, Projection targetProjection, out IPolylineCollection plines, out ObservableTable table, out PolylineRenderLayer renderLayer)
	{
		return RenderLayerFrom(filename, filename, targetProjection, out plines, out table, out renderLayer);
	}

	/// <summary>Reads source bytes from <paramref name="filename"/> while using <paramref name="formatHintPath"/> only to dispatch the retained format.</summary>
	/// <remarks>A distinct format hint must be validated by the caller against the exact persisted source row.</remarks>
	public static OperationResult RenderLayerFrom(string filename, string formatHintPath, Projection targetProjection, out IPolylineCollection plines, out ObservableTable table, out PolylineRenderLayer renderLayer)
	{
		plines = null;
		table = null;
		renderLayer = null;
		OperationResult operationResult;
		if (filename.RoughlyEndsWith(".shp"))
		{
			operationResult = ShapefileIO.TryRead(filename, out PolylineCollectionFeatureCollection collection, targetProjection, (ProgressReporter)null);
			if (!operationResult.Result)
			{
				return operationResult;
			}
			plines = collection.ToExplodedPolylineCollection();
			table = new ObservableTable(collection.AttributeTable);
		}
		else
		{
			if (!GeoJsonReader.HasJsonFileExtension(formatHintPath))
			{
				throw new NotImplementedException("Unsupported/unrecognized file format.");
			}
			operationResult = GeoJsonReader.TryRead(filename, out PolylineFeatureCollection collection2);
			if (!operationResult.Result)
			{
				return operationResult;
			}
			plines = collection2.AsPolylineCollection();
			if (targetProjection != null)
			{
				using ReprojectionTransformer reprojectionTransformer = new ReprojectionTransformer(WellKnownProjections.WGS84, targetProjection);
				foreach (Polyline pline in plines)
				{
					Memory<Point> pointBufferUnsafe = pline.GetPointBufferUnsafe();
					reprojectionTransformer.Transform(pointBufferUnsafe, pointBufferUnsafe);
				}
			}
			table = new ObservableTable(collection2.AttributeTable);
		}
		renderLayer = new PolylineRenderLayer(plines);
		return operationResult;
	}

	protected override int GetFeatureHash(Polyline feature)
	{
		return feature.GetDataHash();
	}

	protected override OperationResult WriteShapefile()
	{
		return ShapefileIO.Write(Filename, Polylines, AttributeTable?.Table);
	}

	protected override OperationResult WriteGeoJson()
	{
		List<Polyline> polylines = new List<Polyline>(Polylines);
		return GeoJsonWriter.Write(Filename, polylines, _projection, AttributeTable?.Table);
	}
}
