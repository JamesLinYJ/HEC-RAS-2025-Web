using Ras.Layers.Terrains;

namespace Ras.Observables;

public class ObservableDebugTerrainInput : ObservableTerrainInput
{
	private DebugTerrainInput _input;

	public override ITerrainInput TerrainInput => _input;

	public ObservableDebugTerrainInput(DebugTerrainInput input)
	{
		_input = input;
	}
}
