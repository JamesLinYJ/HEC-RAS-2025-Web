using System.Collections.Generic;
using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Utility.Attributes;
using Visual.Mapping.Observables.Map;
using Visual.Mapping.Observables.Symbology;

namespace Visual.Mapping.Observables;

[Identifier("PolylineLayer")]
public class PolylineLayerVisualContext : VectorVisualContextBase, IPolylineVectorContext, IVectorContext<Polyline>, IVectorFeatureContext
{
	private const string LINE_SYMBOLOGY_ID = "Polyline_Line_Symbology";

	private const string VERTEX_SYMBOLOGY_ID = "Polyline_Vertex_Symbology";

	private const string LINE_SYMBOLOGY_HEADER = "Line";

	private const string VERTEX_SYMBOLOGY_HEADER = "Vertex";

	private const string POLYLINE_LAYER_NAME = "Generic Polyline Layer";

	private static readonly VectorSymbologyDefinition _defaultLineSymbDef = new VectorSymbologyDefinition("Polyline_Line_Symbology", "Line", VectorType.Line, DefaultEditSymbology.PolylineLineSymbology, "VectorSymbology", "Generic Polyline Layer");

	private static readonly VectorSymbologyDefinition _defaultVertexSymbDef = new VectorSymbologyDefinition("Polyline_Vertex_Symbology", "Vertex", VectorType.Point, DefaultEditSymbology.PolylineVertexSymbology, "VertexSymbology", "Generic Polyline Layer");

	private static readonly VectorSymbologyDefinition[] _defaultSymbologyDefinitions = new VectorSymbologyDefinition[2] { _defaultLineSymbDef, _defaultVertexSymbDef };

	private readonly string _iconResource = NamedResources.Layer.Default;

	private IVectorRenderLayer _initializedRenderLayer;

	private IPolylineCollection _dataLayer;

	public static VectorSymbologyDefinition[] DefaultSymbologyDefinitions => _defaultSymbologyDefinitions;

	public override string IconResource => _iconResource;

	public override string DefaultName => "Polyline Layer";

	public override PolylineRenderLayer RenderLayer => base.RenderLayer as PolylineRenderLayer;

	public SelectedSet SelectedFeatureIDs => GetRenderLayerInternal().SelectedSet;

	public IPolylineCollection DataLayer => _dataLayer;

	IPolylineCollection IPolylineVectorContext.Polylines => DataLayer;

	protected override string ToolTag => "Polyline";

	public PolylineLayerVisualContext(IPolylineCollection plc, MapBase map)
		: base(map)
	{
		_dataLayer = plc;
		Context = plc;
	}

	protected override IReadOnlyList<ToolNode> BuildAdditionalTools()
	{
		return StandardToolFactories.PolylineGeoprocessingTools(this, DataLayer, SelectedFeatureIDs, "Polyline", null, null, DeepConnectSupport.MakeSupplier(Map, DataLayer));
	}

	protected override IRenderLayer CreateRenderLayer()
	{
		return GetRenderLayerInternal();
	}

	private IVectorRenderLayer GetRenderLayerInternal()
	{
		if (_initializedRenderLayer == null)
		{
			_initializedRenderLayer = new PolylineRenderLayer(_dataLayer);
			((PolylineRenderLayer)_initializedRenderLayer).DeserializeSymbologyOptionsGeneric(ToDeserializeElement, SymbologyOptions, _defaultSymbologyDefinitions);
		}
		return _initializedRenderLayer;
	}
}
