using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics.Culverts;

namespace Ras.Observables;

public sealed class ObservableBarrelProperties : ObservableObject, IObservableFeature<BarrelProperties>, INamedObservableFeature, INotifyPropertyChanged
{
	private readonly BarrelProperties _barrelProperties;

	public BarrelProperties BaseFeature => _barrelProperties;

	public string Name
	{
		get
		{
			return _barrelProperties.Name;
		}
		set
		{
			SetProperty(_barrelProperties.Name, value, _barrelProperties, (BarrelProperties b, string v) =>
			{
				b.Name = v;
			}, "Name");
		}
	}

	public double Rise
	{
		get
		{
			return _barrelProperties.Rise;
		}
		set
		{
			SetProperty(_barrelProperties.Rise, value, _barrelProperties, (BarrelProperties b, double v) =>
			{
				b.Rise = v;
			}, "Rise");
		}
	}

	public double Span
	{
		get
		{
			return _barrelProperties.Span;
		}
		set
		{
			SetProperty(_barrelProperties.Span, value, _barrelProperties, (BarrelProperties b, double v) =>
			{
				b.Span = v;
			}, "Span");
		}
	}

	public BarrelShape Shape
	{
		get
		{
			return _barrelProperties.Shape;
		}
		set
		{
			SetProperty(_barrelProperties.Shape, value, _barrelProperties, (BarrelProperties b, BarrelShape v) =>
			{
				b.Shape = v;
			}, "Shape");
		}
	}

	public double Mannings
	{
		get
		{
			return _barrelProperties.Mannings;
		}
		set
		{
			SetProperty(_barrelProperties.Mannings, value, _barrelProperties, (BarrelProperties b, double v) =>
			{
				b.Mannings = v;
			}, "Mannings");
		}
	}

	public bool UseTwoPhaseMannings => _barrelProperties.UseTwoPhaseMannings;

	public double ManningsBot
	{
		get
		{
			return _barrelProperties.ManningsBot;
		}
		set
		{
			SetProperty(_barrelProperties.ManningsBot, value, _barrelProperties, (BarrelProperties b, double v) =>
			{
				b.ManningsBot = v;
			}, "ManningsBot");
		}
	}

	public double DepthBlocked
	{
		get
		{
			return _barrelProperties.DepthBlocked;
		}
		set
		{
			SetProperty(_barrelProperties.DepthBlocked, value, _barrelProperties, (BarrelProperties b, double v) =>
			{
				b.DepthBlocked = v;
			}, "DepthBlocked");
		}
	}

	public double ManningsSwitchDepth
	{
		get
		{
			return _barrelProperties.ManningsSwitchDepth;
		}
		set
		{
			SetProperty(_barrelProperties.ManningsSwitchDepth, value, _barrelProperties, (BarrelProperties b, double v) =>
			{
				b.ManningsSwitchDepth = v;
			}, "ManningsSwitchDepth");
		}
	}

	public Extent Extent => _barrelProperties.Extent;

	public ObservableBarrelProperties(BarrelProperties barrelProperties)
	{
		_barrelProperties = barrelProperties;
	}

	public ObservableBarrelProperties()
	{
		_barrelProperties = new BarrelProperties();
	}

	public override string ToString()
	{
		return Name;
	}
}
