using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rasters;
using Geospatial.Vectors.Rasterizers;
using Ras.Mapping;
using Ras.Rendering;
using Utility.Interfaces;

namespace Ras.Observables;

public class ObservableResultMap : ObservableObject, IReadOnlyDisplayName
{
	private readonly ResultMap _map;

	public ResultMap Map => _map;

	string IReadOnlyDisplayName.DisplayName => $"{Result.Name} {Type}";

	public ObservableResult Result { get; }

	public ObservableProject Project { get; }

	public MapTypes Type => _map.Type;

	public GenericMapType GenericType => _map.GenericMapType;

	public IResample<float> Terrain => _map.Terrain;

	public string DefaultExportFilename => _map.Type.ToString() + " (Exported).tif";

	public string UnitName { get; set; }

	public ITimeSeriesResampler<float> AnalyticalSurface { get; set; }

	public ObservableResultMap(ObservableResult res, ResultMap map)
	{
		Result = res;
		Project = res.Project;
		_map = map;
	}
}
