using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers.Classifiers;

namespace Ras.Observables;

public abstract class ObservableClassificationInput : ObservableObject, IObservableClassificationInput
{
	public string Name
	{
		get
		{
			return ClassificationInput.Name;
		}
		set
		{
			SetProperty(ClassificationInput.Name, value, ClassificationInput, (IClassificationInput model, string newVal) =>
			{
				model.Name = newVal;
			}, "Name");
		}
	}

	public abstract IClassificationInput ClassificationInput { get; }

	public abstract event IObservableClassificationInput.ClassMapRebuiltEvent ClassMapRebuilt;
}
