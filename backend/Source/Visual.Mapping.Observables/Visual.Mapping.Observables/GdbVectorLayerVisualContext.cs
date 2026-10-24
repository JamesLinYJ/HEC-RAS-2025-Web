using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Memory;
using Visual.Mapping.Observables.Map;
using Visual.Observables;
using Visual.Observables.Notifications;

namespace Visual.Mapping.Observables;

public class GdbVectorLayerVisualContext : AsyncVectorMapLayerVisualContext
{
	private static readonly ILogger _logger = LogHelper.GetLogger("GdbVectorLayerVisualContext");

	private const string PointSymbologyId = "Gdb_Point_Symbology";

	private const string PointSymbologyHeader = "Point";

	private const string GdbLayerName = "GDB Vector";

	protected static readonly VectorSymbologyDefinition _defaultPointSymbDef = new VectorSymbologyDefinition("Gdb_Point_Symbology", "Point", Geospatial.Rendering.Systems.Vector.VectorType.Point, DefaultEditSymbology.PointMapLayerSymbology, "VectorSymbology", "GDB Vector");

	private const string LineSymbologyId = "Gdb_Line_Symbology";

	private const string LineSymbologyHeader = "Line";

	protected static readonly VectorSymbologyDefinition _defaultLineSymbDef = new VectorSymbologyDefinition("Gdb_Line_Symbology", "Line", Geospatial.Rendering.Systems.Vector.VectorType.Line, DefaultEditSymbology.PolylineMapLayerLineSymbology, "VectorSymbology", "GDB Vector");

	private const string LineVertexSymbologyId = "Gdb_Line_Vertex_Symbology";

	private const string LineVertexSymbologyHeader = "Vertex";

	protected static readonly VectorSymbologyDefinition _defaultLineVertexSymbDef = new VectorSymbologyDefinition("Gdb_Line_Vertex_Symbology", "Vertex", Geospatial.Rendering.Systems.Vector.VectorType.Point, DefaultEditSymbology.PolylineMapLayerVertexSymbology, "VertexSymbology", "GDB Vector");

	private const string PolygonSymbologyId = "Gdb_Polygon_Symbology";

	private const string PolygonSymbologyHeader = "Polygon";

	protected static readonly VectorSymbologyDefinition _defaultPolygonSymbDef = new VectorSymbologyDefinition("Gdb_Polygon_Symbology", "Polygon", Geospatial.Rendering.Systems.Vector.VectorType.Polygon, DefaultEditSymbology.PolygonMapLayerShapeSymbology, "VectorSymbology", "GDB Vector");

	public const string GdbVectorLayerXmlNodeIdentifier = "GdbVectorLayer";

	private readonly string _layerName;

	private readonly Geospatial.GDALAssist.VectorType _vectorType;

	private Projection _projection;

	public static VectorSymbologyDefinition[] DefaultSymbologyDefinitions => new VectorSymbologyDefinition[4] { _defaultPointSymbDef, _defaultLineSymbDef, _defaultLineVertexSymbDef, _defaultPolygonSymbDef };

	public override string XmlNodeIdentifier => "GdbVectorLayer";

	public override string DefaultName => "GDB Layer";

	public string LayerName => _layerName;

	public Geospatial.GDALAssist.VectorType VectorGeometryType => _vectorType;

	public VectorRenderLayer VectorLayer => RenderLayer as VectorRenderLayer;

	protected Projection TargetProjection => _projection;

	protected GdbVectorLayerVisualContext(string gdbPath, string layerName, Geospatial.GDALAssist.VectorType vectorType, Projection projection, MapBase map)
		: base(gdbPath, map)
	{
		_layerName = layerName;
		_vectorType = vectorType;
		_projection = projection;
		Name = layerName;
	}

	public static GdbVectorLayerVisualContext Create(string gdbPath, string layerName, Geospatial.GDALAssist.VectorType vectorType, Projection projection, MapBase map)
	{
		switch (vectorType)
		{
		case Geospatial.GDALAssist.VectorType.Point:
		case Geospatial.GDALAssist.VectorType.PointCollection:
			return new GdbPointLayerVisualContext(gdbPath, layerName, vectorType, projection, map);
		case Geospatial.GDALAssist.VectorType.Line:
		case Geospatial.GDALAssist.VectorType.LineCollection:
			return new GdbPolylineLayerVisualContext(gdbPath, layerName, vectorType, projection, map);
		case Geospatial.GDALAssist.VectorType.Polygon:
		case Geospatial.GDALAssist.VectorType.PolygonCollection:
			return new GdbPolygonLayerVisualContext(gdbPath, layerName, vectorType, projection, map);
		default:
			return new GdbVectorLayerVisualContext(gdbPath, layerName, vectorType, projection, map);
		}
	}

	protected override EditingSession CreateEditLayerCore(ToolOrchestrator editLayer)
	{
		return null;
	}

	protected override IReadOnlyList<ToolNode> BuildContributedToolsInternal()
	{
		return BuildSelectionTools();
	}

	public override void Serialize(XElement serializeTo)
	{
		serializeTo.SetAttributeValue("GdbLayerName", _layerName);
		serializeTo.SetAttributeValue("GdbVectorType", _vectorType.ToString());
		base.Serialize(serializeTo);
	}

	protected override void LoadInBackground()
	{
		try
		{
			using VectorDataset vectorDataset = new VectorDataset(Filename);
			using VectorLayer vectorLayer = vectorDataset.GetLayer(_layerName);
			Projection projection = vectorLayer.GetProjection();
			Table attrTable = vectorLayer.ReadAttributeTable();
			IRenderLayer rl = BuildRenderLayer(vectorLayer, projection);
			if (rl != null)
			{
				VectorSymbologyDefinition[] symbDefs = GetSymbologyDefinitions();
				Map.UIThreadSyncContext.Sync(() =>
				{
					PublishLoaded(rl, new ObservableTable(attrTable), symbDefs);
				});
			}
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to load GDB layer {LayerName} from {Filename}", _layerName, Filename);
			ToastNotifiers.Default.SendError("Failed to load GDB layer '" + _layerName + "'");
		}
	}

	protected virtual IRenderLayer BuildRenderLayer(VectorLayer layer, Projection srcProj)
	{
		_logger.LogWarning("Unsupported GDB vector type: {VectorType}", _vectorType);
		return null;
	}

	protected virtual VectorSymbologyDefinition[] GetSymbologyDefinitions()
	{
		return Array.Empty<VectorSymbologyDefinition>();
	}

	protected bool NeedsReprojection(Projection srcProj)
	{
		if (srcProj == null || _projection == null)
		{
			return false;
		}
		return !srcProj.IsEqual(_projection);
	}
}
