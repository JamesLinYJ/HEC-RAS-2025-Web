using System.Reflection;
using Geospatial.Rendering.Layers.Attributes;

namespace Visual.Mapping.Observables;

public class ObservableEnumPlotOption : ObservablePlotOption
{
	public object Value
	{
		get
		{
			return _propertyInfo.GetValue(_owner);
		}
		set
		{
			_propertyInfo.SetValue(_owner, value);
			OnPropertyChanged("Value");
		}
	}

	public ObservableEnumPlotOption(PlotOption plotOption, PropertyInfo info, object owner)
		: base(plotOption, info, owner)
	{
	}

	public override void PlotOptionChanged()
	{
		OnPropertyChanged("Value");
	}
}
