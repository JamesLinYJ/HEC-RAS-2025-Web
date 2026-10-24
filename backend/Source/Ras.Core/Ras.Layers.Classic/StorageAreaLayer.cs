using System.Diagnostics;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Storage Areas")]
public class StorageAreaLayer : ClassicGeometryLayerBase<StorageArea>
{
	public static readonly string HDF5GroupName = "/Geometry/Storage Areas";

	private IPolygonCollection _plWrapper;

	protected override string HDF5Group => HDF5GroupName;

	internal StorageAreaLayer(ClassicGeometry geom)
		: base(geom)
	{
	}

	public IPolygonCollection AsPolygonCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolygonFtLayerWrapped<StorageArea>(this, (StorageArea sa) => sa.Polygon, (Polygon pl) => new StorageArea(pl));
		}
		return _plWrapper;
	}
}
