namespace Ras.Layers.Helpers;

public class GeometryInfo : CoreLayerInfo
{
	public MeshInfo MeshInfo { get; set; }

	public CulvertInfo CulvertInfo { get; set; }

	public static GeometryInfo From(string filename, CoreFileOpenOptions opts = null)
	{
		using Geometry geom = new Geometry(filename);
		return From(geom, opts);
	}

	public static GeometryInfo From(Geometry geom, CoreFileOpenOptions opts = null)
	{
		opts = opts ?? CoreFileOpenOptions.Default();
		GeometryInfo geometryInfo = new GeometryInfo();
		geometryInfo.Init(geom, opts);
		geometryInfo.MeshInfo = new MeshInfo(geom.Mesh);
		geometryInfo.CulvertInfo = new CulvertInfo(geom);
		return geometryInfo;
	}
}
