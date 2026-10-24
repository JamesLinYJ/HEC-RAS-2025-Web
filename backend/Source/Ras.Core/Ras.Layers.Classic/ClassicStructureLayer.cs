using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.Vectors;
using Ras.Hydraulics.Classic;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Structures")]
public class ClassicStructureLayer : ClassicGeometryLayerBase<Structure>
{
	public const string HDF5GroupName = "/Geometry/Structures";

	private IPolylineCollection _plWrapper;

	protected override string HDF5Group => "/Geometry/Structures";

	internal ClassicStructureLayer(ClassicGeometry geom)
		: base(geom)
	{
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolylineFtLayerWrapped<Structure>(this, (Structure riv) => riv.Polyline, (Polyline pl) => new Structure
			{
				Polyline = pl
			});
		}
		return _plWrapper;
	}

	public Structure FindStructure(StructureIdentifier id)
	{
		IReadOnlyList<Structure> features = Features;
		for (int i = 0; i < features.Count; i++)
		{
			if (id.Matches(features[i].ID))
			{
				return features[i];
			}
		}
		return null;
	}
}
