using System;
using Geospatial.Vectors;
using Ras.Hydraulics.Culverts;
using Ras.Layers;
using Utility.Interfaces;

namespace Ras.Observables;

public class ObservableCulvertBarrelLayer : ObservableGeometryLayer<CulvertBarrelLayer, CulvertBarrel, ObservableCulvertBarrel>, IReadOnlyDisplayName
{
	private IPolylineCollection _plWrapper;

	public string DisplayName => "Culvert Barrels";

	public ObservableCulvertBarrelLayer(ObservableGeometry geometry)
		: base(geometry, geometry.Geometry.CulvertBarrelLayer, (Func<CulvertBarrel, ObservableCulvertBarrel>)((CulvertBarrel barrel) => new ObservableCulvertBarrel(barrel)))
	{
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new ObservablePolylineFtLayerWrapped<CulvertBarrelLayer, CulvertBarrel, ObservableCulvertBarrel>(this, (CulvertBarrel barrel) => barrel.Polyline, (Polyline pl) => new CulvertBarrel
			{
				Polyline = pl,
				Name = "Culvert Barrel"
			});
		}
		return _plWrapper;
	}
}
