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

[Identifier("Polygon")]
public class PolygonLayerVisualContext : VectorVisualContextBase, IPolygonVectorContext, IVectorContext<Polygon>, IVectorFeatureContext
{
	private const string POLYGON_SYMBOLOGY_ID = "Polygon_Shape_Symbology";

	private const string VERTEX_SYMBOLOGY_ID = "Polygon_Vertex_Symbology";

	private const string POLYGON_SYMBOLOGY_HEADER = "Polygon";

	private const string VERTEX_SYMBOLOGY_HEADER = "Vertex";

	private const string POLYGON_LAYER_NAME = "Generic Polygon Layer";

	private static readonly VectorSymbologyDefinition _defaultPolygonSymbDef = new VectorSymbologyDefinition("Polygon_Shape_Symbology", "Polygon", VectorType.Polygon, DefaultEditSymbology.PolygonShapeSymbology, "VectorSymbology", "Generic Polygon Layer");

	private static readonly VectorSymbologyDefinition _defaultVertexSymbDef = new VectorSymbologyDefinition("Polygon_Vertex_Symbology", "Vertex", VectorType.Point, DefaultEditSymbology.PolygonVertexSymbology, "VertexSymbology", "Generic Polygon Layer");

	private static readonly VectorSymbologyDefinition[] _defaultSymbologyDefinitions = new VectorSymbologyDefinition[2] { _defaultPolygonSymbDef, _defaultVertexSymbDef };

	private readonly string _iconResource = NamedResources.Layer.Default;

	private IPolygonCollection _dataLayer;

	public static VectorSymbologyDefinition[] DefaultSymbologyDefinitions => _defaultSymbologyDefinitions;

	public override string IconResource => _iconResource;

	public override string DefaultName => "Polygon Layer";

	public override IPolygonCollection Context => base.Context as IPolygonCollection;

	public override PolygonRenderLayer RenderLayer => base.RenderLayer as PolygonRenderLayer;

	public SelectedSet SelectedFeatureIDs => RenderLayer.SelectedSet;

	public IPolygonCollection DataLayer => _dataLayer;

	IPolygonCollection IPolygonVectorContext.Polygons => DataLayer;

	protected override string ToolTag => "Polygon";

	public PolygonLayerVisualContext(IPolygonCollection plc, MapBase map)
		: base(map)
	{
		_dataLayer = plc;
		base.Context = plc;
	}

	protected override IRenderLayer CreateRenderLayer()
	{
		return RenderLayerFrom(_dataLayer);
	}

	public PolygonRenderLayer RenderLayerFrom(IEnumerable<Polygon> pgons)
	{
		PolygonRenderLayer polygonRenderLayer = new PolygonRenderLayer(pgons);
		polygonRenderLayer.DeserializeSymbologyOptionsGeneric(ToDeserializeElement, SymbologyOptions, _defaultSymbologyDefinitions);
		return polygonRenderLayer;
	}
}
