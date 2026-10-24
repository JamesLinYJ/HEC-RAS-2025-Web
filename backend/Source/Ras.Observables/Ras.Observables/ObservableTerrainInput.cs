using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers.Terrains;
using Utility.Validation;

namespace Ras.Observables;

public abstract class ObservableTerrainInput : ObservableValidator, IObservableTerrainInput, IMutableNotifyDataErrorInfo, INotifyDataErrorInfo
{
	public abstract ITerrainInput TerrainInput { get; }

	public string Name
	{
		get
		{
			return TerrainInput.Name;
		}
		set
		{
			SetProperty(TerrainInput.Name, value, TerrainInput, (ITerrainInput model, string newVal) =>
			{
				model.Name = newVal;
			}, "Name");
		}
	}

	void IMutableNotifyDataErrorInfo.ValidateProperty(object localValue, string propertyName)
	{
		ValidateProperty(localValue, propertyName);
	}
}
