using Ras.Layers.Terrains;

namespace Ras.Observables;

public class ObservableConstantTerrainInput : ObservableTerrainInput
{
	private ConstantTerrainInput _input;

	public override ITerrainInput TerrainInput => _input;

	public ObservableConstantTerrainInput(ConstantTerrainInput input)
	{
		_input = input;
	}
}
