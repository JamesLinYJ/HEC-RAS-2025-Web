using Ras.Layers.Classifiers;

namespace Ras.Observables;

public interface IObservableClassificationInput
{
	public delegate void ClassMapRebuiltEvent(IObservableClassificationInput sender);

	string TypeName => ClassificationInput.TypeName;

	string Name { get; set; }

	IClassificationInput ClassificationInput { get; }

	event ClassMapRebuiltEvent ClassMapRebuilt;
}
