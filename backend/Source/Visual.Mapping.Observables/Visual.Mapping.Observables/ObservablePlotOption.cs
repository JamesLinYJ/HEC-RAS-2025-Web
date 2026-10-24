using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering.Layers.Attributes;

namespace Visual.Mapping.Observables;

public abstract class ObservablePlotOption : ObservableObject
{
	protected object _owner;

	protected PropertyInfo _propertyInfo;
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public PlotOption PlotOption
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<PlotOption>.Default.Equals(field, value))
			{
				OnPropertyChanging("PlotOption");
				OnPropertyChanging("Label");
				field = value;
				OnPlotOptionChanged(value);
				OnPropertyChanged("PlotOption");
				OnPropertyChanged("Label");
			}
		}
	}

	public string Label
	{
		get
		{
			return PlotOption.Label;
		}
		set
		{
			PlotOption.Label = value;
			OnPropertyChanged("Label");
		}
	}

	public ObservablePlotOption(PlotOption plotOption, PropertyInfo info, object owner)
	{
		_propertyInfo = info;
		_owner = owner;
		PlotOption = plotOption;
	}

	public abstract void PlotOptionChanged();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPlotOptionChanged(PlotOption plotOption)
	{
		PlotOptionChanged();
	}
}
