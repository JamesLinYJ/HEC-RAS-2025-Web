using Ras.Layers.Terrains;

namespace Ras.Observables;

public class ObservableRasterTerrainInput : ObservableTerrainInput
{
	private readonly RasterTerrainInput _rasterTerrainInput;

	private ObservableTerrain _terrain;

	public override ITerrainInput TerrainInput => _rasterTerrainInput;

	public ObservableTerrain Terrain => _terrain;

	public string Filename => _rasterTerrainInput.Filename;

	public string TypeName => _rasterTerrainInput.TypeName;

	public ObservableRasterTerrainInput(ObservableTerrain obsTerrain, RasterTerrainInput rasterTerrainInput)
	{
		_terrain = obsTerrain;
		_rasterTerrainInput = rasterTerrainInput;
	}
}
