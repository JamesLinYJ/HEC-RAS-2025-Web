using System;
using System.ComponentModel;
using Ras.Layers.Terrains;
using Utility.Validation;

namespace Ras.Observables;

public interface IObservableTerrainInput : IMutableNotifyDataErrorInfo, INotifyDataErrorInfo
{
	static Type Type => typeof(IObservableTerrainInput);

	ITerrainInput TerrainInput { get; }

	string Name { get; set; }
}
