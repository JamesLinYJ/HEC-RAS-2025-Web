namespace Ras.Layers;

public sealed class GraphGeometryLayer : GraphLayer, IGeometryLayer, IFeatureLayer
{
	public static readonly string HDF5GroupName = "/Geometry/Graph";

	public Geometry Geometry { get; }

	internal GraphGeometryLayer(Geometry geom)
		: base(HDF5GroupName, (GraphLayer model) => (model as GraphGeometryLayer).Geometry?.Name)
	{
		Geometry = geom;
	}
}
