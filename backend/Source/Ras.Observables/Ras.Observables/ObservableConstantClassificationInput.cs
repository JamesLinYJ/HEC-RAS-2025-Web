using Ras.Layers.Classifiers;

namespace Ras.Observables;

public class ObservableConstantClassificationInput : ObservableClassificationInput
{
	private ConstantClassificationInput _input;

	public override IClassificationInput ClassificationInput => _input;

	public override event IObservableClassificationInput.ClassMapRebuiltEvent ClassMapRebuilt
	{
		add
		{
		}
		remove
		{
		}
	}

	public ObservableConstantClassificationInput(ConstantClassificationInput input)
	{
		_input = input;
	}
}
