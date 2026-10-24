using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics.Culverts;
using Utility.Validation;

namespace Ras.Observables;

public sealed class ObservableOpeningProperties : ObservableValidator, IObservableFeature<OpeningProperties>, INamedObservableFeature, INotifyPropertyChanged
{
	private readonly OpeningProperties _openingProperties;

	public OpeningProperties BaseFeature => _openingProperties;

	public string Name
	{
		get
		{
			return _openingProperties.Name;
		}
		set
		{
			SetProperty(_openingProperties.Name, value, _openingProperties, (OpeningProperties o, string v) =>
			{
				o.Name = v;
			}, "Name");
		}
	}

	[DisplayName("K Entrance")]
	[AbsoluteBounds(0.0, 1.0, null)]
	public double KIn
	{
		get
		{
			return _openingProperties.KIn;
		}
		set
		{
			SetProperty(_openingProperties.KIn, value, _openingProperties, (OpeningProperties o, double v) =>
			{
				o.KIn = v;
			}, "KIn");
		}
	}

	[AbsoluteBounds(0.0, 1.0, null)]
	public double KOut
	{
		get
		{
			return _openingProperties.KOut;
		}
		set
		{
			SetProperty(_openingProperties.KOut, value, _openingProperties, (OpeningProperties o, double v) =>
			{
				o.KOut = v;
			}, "KOut");
		}
	}

	public BarrelOpeningType OpeningType
	{
		get
		{
			return _openingProperties.OpeningType;
		}
		set
		{
			SetProperty(_openingProperties.OpeningType, value, _openingProperties, (OpeningProperties o, BarrelOpeningType v) =>
			{
				o.OpeningType = v;
			}, "OpeningType");
		}
	}

	public Extent Extent => _openingProperties.Extent;

	public (int Chart, int Scale) ChartScale => _openingProperties.TryGetChartScale();

	public ObservableOpeningProperties(OpeningProperties openingProperties)
	{
		_openingProperties = openingProperties;
	}

	public ObservableOpeningProperties()
	{
		_openingProperties = new OpeningProperties();
	}

	public override string ToString()
	{
		return Name;
	}
}
