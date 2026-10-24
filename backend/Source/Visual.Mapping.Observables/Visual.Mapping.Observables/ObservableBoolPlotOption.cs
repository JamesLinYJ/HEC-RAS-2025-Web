using System.Reflection;
using Geospatial.Rendering.Layers.Attributes;

namespace Visual.Mapping.Observables;

public class ObservableBoolPlotOption : ObservablePlotOption
{
	public bool Value
	{
		get
		{
			return (bool)_propertyInfo.GetValue(_owner);
		}
		set
		{
			_propertyInfo.SetValue(_owner, value);
			OnPropertyChanged("Value");
		}
	}

	public ObservableBoolPlotOption(PlotOption plotOption, PropertyInfo info, object owner)
		: base(plotOption, info, owner)
	{
	}

	public override void PlotOptionChanged()
	{
		OnPropertyChanged("Value");
	}
}
