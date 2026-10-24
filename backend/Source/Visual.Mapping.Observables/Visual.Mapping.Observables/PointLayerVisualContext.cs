using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Utility.Attributes;
using Visual.Mapping.Observables.Map;
using Visual.Mapping.Observables.Symbology;

namespace Visual.Mapping.Observables;

[Identifier("PointLayer")]
public class PointLayerVisualContext : VectorVisualContextBase, IPointVectorContext, IVectorContext<Point>, IVectorFeatureContext
{
	private const string POINT_SYMBOLOGY_ID = "Point_Symbology";

	private const string POINT_SYMBOLOGY_HEADER = "Point";

	private const string POINT_LAYER_NAME = "Generic Point Layer";

	private static readonly VectorSymbologyDefinition _defaultPointSymbDef = new VectorSymbologyDefinition("Point_Symbology", "Point", VectorType.Point, DefaultEditSymbology.PointSymbology, "VectorSymbology", "Generic Point Layer");

	private static readonly VectorSymbologyDefinition[] _defaultSymbologyDefinitions = new VectorSymbologyDefinition[1] { _defaultPointSymbDef };

	private readonly string _iconResource = NamedResources.Layer.Default;

	private IVectorRenderLayer _initializedRenderLayer;

	private IPointCollection _dataLayer;

	public static VectorSymbologyDefinition[] DefaultSymbologyDefinitions => _defaultSymbologyDefinitions;

	public override string IconResource => _iconResource;

	public override string DefaultName => "Point Layer";

	public override PointRenderLayer RenderLayer => base.RenderLayer as PointRenderLayer;

	public SelectedSet SelectedFeatureIDs => GetRenderLayerInternal().SelectedSet;

	public IPointCollection DataLayer => _dataLayer;

	IPointCollection IPointVectorContext.Points => DataLayer;

	public PointLayerVisualContext(IPointCollection plc, MapBase map)
		: base(map)
	{
		_dataLayer = plc;
		Context = plc;
	}

	protected override IRenderLayer CreateRenderLayer()
	{
		return GetRenderLayerInternal();
	}

	private IVectorRenderLayer GetRenderLayerInternal()
	{
		if (_initializedRenderLayer == null)
		{
			_initializedRenderLayer = new PointRenderLayer(_dataLayer);
			_initializedRenderLayer.DeserializeSymbologyOptionsGeneric(ToDeserializeElement, SymbologyOptions, _defaultSymbologyDefinitions);
		}
		return _initializedRenderLayer;
	}
}
