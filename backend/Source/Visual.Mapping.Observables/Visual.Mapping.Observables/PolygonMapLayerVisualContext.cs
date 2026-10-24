using System;
using System.Collections.Generic;
using System.Linq;
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
using Visual.Observables.Notifications;

namespace Visual.Mapping.Observables;

public class PolygonMapLayerVisualContext : VectorFileMapLayerVisualContext<IPolygonCollection, Polygon>, IPolygonVectorContext, IVectorContext<Polygon>, IVectorFeatureContext
{
	private const string POLYGON_SYMBOLOGY_ID = "Polygon_Map_Layer_Shape_Symbology";

	private const string VERTEX_SYMBOLOGY_ID = "Polygon_Map_Layer_Vertex_Symbology";

	private const string POLYGON_SYMBOLOGY_HEADER = "Polygon";

	private const string VERTEX_SYMBOLOGY_HEADER = "Vertex";

	private const string POLYGON_MAP_LAYER_NAME = "Polygon Map Layer";

	private static readonly VectorSymbologyDefinition _defaultPolygonSymbDef = new VectorSymbologyDefinition("Polygon_Map_Layer_Shape_Symbology", "Polygon", Geospatial.Rendering.Systems.Vector.VectorType.Polygon, DefaultEditSymbology.PolygonMapLayerShapeSymbology, "VectorSymbology", "Polygon Map Layer");

	private static readonly VectorSymbologyDefinition _defaultVertexSymbDef = new VectorSymbologyDefinition("Polygon_Map_Layer_Vertex_Symbology", "Vertex", Geospatial.Rendering.Systems.Vector.VectorType.Point, DefaultEditSymbology.PolygonMapLayerVertexSymbology, "VertexSymbology", "Polygon Map Layer");

	private static readonly VectorSymbologyDefinition[] _defaultSymbologyDefinitions = new VectorSymbologyDefinition[2] { _defaultPolygonSymbDef, _defaultVertexSymbDef };

	private static readonly ILogger _logger = LogHelper.GetLogger("PolygonMapLayerVisualContext");

	public const string PolygonsXmlNodeIdentifier = "PolygonsVector";

	public static VectorSymbologyDefinition[] DefaultSymbologyDefinitions => _defaultSymbologyDefinitions;

	protected override VectorSymbologyDefinition[] SymbologyDefinitions => _defaultSymbologyDefinitions;

	public static Type Type => typeof(PolygonMapLayerVisualContext);

	public override string XmlNodeIdentifier => "PolygonsVector";

	public override string DefaultName => "Polygon Layer";

	public IPolygonCollection Polygons { get; private set; }

	protected override IPolygonCollection FeatureCollection => Polygons;

	public override PolygonRenderLayer RenderLayer => base.RenderLayer as PolygonRenderLayer;

	protected override string ToolTag => "PolygonMap";

	public PolygonMapLayerVisualContext(string filename, Projection projection, MapBase map)
		: base(filename, projection, map)
	{
	}

	protected override OperationResult LoadFromFile(out IPolygonCollection features, out ObservableTable table, out IRenderLayer renderLayer)
	{
		OperationResult result = RenderLayerFrom(Filename, _projection, out features, out table, out var polygonRenderLayer);
		renderLayer = polygonRenderLayer;
		return result;
	}

	protected override void StoreLoadedFeatures(IPolygonCollection features, ObservableTable table, bool rowsMatchFeatures)
	{
		IPolygonCollection polygons;
		if (!rowsMatchFeatures)
		{
			polygons = features;
		}
		else
		{
			IPolygonCollection polygonCollection = new ObservablePolygonFeatureCollection(features, table);
			polygons = polygonCollection;
		}
		Polygons = polygons;
	}

	protected override ObservableTable SymbologyAttributeTable(ObservableTable table, bool rowsMatchFeatures)
	{
		if (!rowsMatchFeatures)
		{
			return null;
		}
		return table;
	}

	public static OperationResult RenderLayerFrom(string filename, Projection targetProjection, out IPolygonCollection pgons, out ObservableTable table, out PolygonRenderLayer polygonRenderLayer)
	{
		return RenderLayerFrom(filename, filename, targetProjection, out pgons, out table, out polygonRenderLayer);
	}

	/// <summary>Reads source bytes from <paramref name="filename"/> while using <paramref name="formatHintPath"/> only to dispatch the retained format.</summary>
	/// <remarks>A distinct format hint must be validated by the caller against the exact persisted source row.</remarks>
	public static OperationResult RenderLayerFrom(string filename, string formatHintPath, Projection targetProjection, out IPolygonCollection pgons, out ObservableTable table, out PolygonRenderLayer polygonRenderLayer)
	{
		pgons = null;
		table = null;
		polygonRenderLayer = null;
		OperationResult operationResult;
		if (filename.RoughlyEndsWith(".shp"))
		{
			operationResult = ShapefileIO.TryRead(filename, out PolygonCollectionFeatureCollection collection, targetProjection, (ProgressReporter)null);
			if (!operationResult.Result)
			{
				return operationResult;
			}
			table = new ObservableTable(collection.AttributeTable);
			pgons = collection.ToExplodedPolygonCollection();
		}
		else
		{
			if (!GeoJsonReader.HasJsonFileExtension(formatHintPath))
			{
				throw new NotImplementedException("Unsupported/unrecognized file format.");
			}
			operationResult = GeoJsonReader.TryRead(filename, out PolygonFeatureCollection collection2);
			if (!operationResult.Result)
			{
				return operationResult;
			}
			pgons = collection2.AsPolygonCollection();
			if (targetProjection != null)
			{
				using ReprojectionTransformer reprojectionTransformer = new ReprojectionTransformer(WellKnownProjections.WGS84, targetProjection);
				foreach (Polygon pgon in pgons)
				{
					Memory<Point> pointBufferUnsafe = pgon.GetPointBufferUnsafe();
					reprojectionTransformer.Transform(pointBufferUnsafe, pointBufferUnsafe);
				}
			}
			table = new ObservableTable(collection2.AttributeTable);
		}
		if (pgons != null)
		{
			polygonRenderLayer = new PolygonRenderLayer(pgons);
		}
		return operationResult;
	}

	public static ComplexPolygonRenderLayer ComplexPolygonRenderLayerFrom(string filename, Projection targetProjection, out IPolygonCollection pgons)
	{
		if (filename.RoughlyEndsWith(".shp"))
		{
			OperationResult operationResult = ShapefileIO.TryRead(filename, out PolygonCollectionFeatureCollection collection, targetProjection, (ProgressReporter)null);
			if (!operationResult.Result)
			{
				ToastNotifiers.Default.SendAggregatedMessages(operationResult);
				pgons = null;
				return null;
			}
			pgons = collection.ToExplodedPolygonCollection();
			ComplexPolygonCollection complexPolygonCollection = new ComplexPolygonCollection(collection.Count);
			HashSet<MessageClass> messageClasss = new HashSet<MessageClass>();
			for (int i = 0; i < collection.Features.Count; i++)
			{
				OperationResult operationResult2 = ComplexPolygon.TryCreateComplexPolygons(collection.Features[i].ToList(), out var complexPolys);
				if (operationResult2.Result)
				{
					complexPolygonCollection.AddRange(complexPolys);
					continue;
				}
				foreach (Message message in operationResult2.Messages)
				{
					messageClasss.Add(message.MessageType);
				}
			}
			return new ComplexPolygonRenderLayer(complexPolygonCollection);
		}
		throw new NotImplementedException("Unsupported/unrecognized file format.");
	}

	protected override int GetFeatureHash(Polygon feature)
	{
		return feature.GetDataHash();
	}

	protected override OperationResult WriteShapefile()
	{
		return ShapefileIO.Write(Filename, Polygons, AttributeTable?.Table);
	}

	protected override OperationResult WriteGeoJson()
	{
		List<Polygon> polygons = new List<Polygon>(Polygons);
		return GeoJsonWriter.Write(Filename, polygons, _projection, AttributeTable?.Table);
	}
}
