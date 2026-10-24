using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rasters;
using Geospatial.Rasters.Modifiers;
using Geospatial.Vectors;
using Utility.Validation;

namespace Ras.Observables.Layers;

public sealed class ObservableLeveeModProperties : ObservableValidator, IObservableFeature<LeveeModProperties>, INamedObservableFeature, INotifyPropertyChanged
{
	private readonly LeveeModProperties _properties;

	public LeveeModProperties BaseFeature => _properties;

	public string Name
	{
		get
		{
			return _properties.Name;
		}
		set
		{
			SetProperty(_properties.Name, value, _properties, (LeveeModProperties p, string v) =>
			{
				p.Name = v;
			}, "Name");
		}
	}

	[AbsoluteBounds(0.0, double.MaxValue, null)]
	public double TopWidth
	{
		get
		{
			return _properties.TopWidth;
		}
		set
		{
			SetProperty(_properties.TopWidth, value, _properties, (LeveeModProperties p, double v) =>
			{
				p.TopWidth = v;
			}, "TopWidth");
		}
	}

	public double LeftGrade
	{
		get
		{
			return _properties.LeftGrade;
		}
		set
		{
			if (SetProperty(_properties.LeftGrade, value, _properties, (LeveeModProperties p, double v) =>
			{
				p.LeftGrade = v;
			}, "LeftGrade"))
			{
				OnPropertyChanged("EffectiveRightGrade");
			}
		}
	}

	public double RightGrade
	{
		get
		{
			return _properties.RightGrade;
		}
		set
		{
			if (SetProperty(_properties.RightGrade, value, _properties, (LeveeModProperties p, double v) =>
			{
				p.RightGrade = v;
			}, "RightGrade"))
			{
				OnPropertyChanged("EffectiveRightGrade");
			}
		}
	}

	public double EffectiveRightGrade => _properties.EffectiveRightGrade;

	[AbsoluteBounds(0.0, double.MaxValue, null)]
	public double LeftWidth
	{
		get
		{
			return _properties.LeftWidth;
		}
		set
		{
			if (SetProperty(_properties.LeftWidth, value, _properties, (LeveeModProperties p, double v) =>
			{
				p.LeftWidth = v;
			}, "LeftWidth"))
			{
				OnPropertyChanged("EffectiveRightWidth");
			}
		}
	}

	[AbsoluteBounds(0.0, double.MaxValue, null)]
	public double RightWidth
	{
		get
		{
			return _properties.RightWidth;
		}
		set
		{
			if (SetProperty(_properties.RightWidth, value, _properties, (LeveeModProperties p, double v) =>
			{
				p.RightWidth = v;
			}, "RightWidth"))
			{
				OnPropertyChanged("EffectiveRightWidth");
			}
		}
	}

	public double EffectiveRightWidth => _properties.EffectiveRightWidth;

	public LeveeMode Mode
	{
		get
		{
			return _properties.Mode;
		}
		set
		{
			if (SetProperty(_properties.Mode, value, _properties, (LeveeModProperties p, LeveeMode v) =>
			{
				p.Mode = v;
			}, "Mode") && (ModType == RasterModType.TakeHigher || ModType == RasterModType.TakeLower))
			{
				ModType = ((value != LeveeMode.SimpleCut) ? RasterModType.TakeHigher : RasterModType.TakeLower);
			}
		}
	}

	public RasterModType ModType
	{
		get
		{
			return _properties.ModType;
		}
		set
		{
			SetProperty(_properties.ModType, value, _properties, (LeveeModProperties p, RasterModType v) =>
			{
				p.ModType = v;
			}, "ModType");
		}
	}

	public Extent Extent => _properties.Extent;

	public ObservableLeveeModProperties(LeveeModProperties properties)
	{
		_properties = properties;
	}

	public ObservableLeveeModProperties()
	{
		_properties = new LeveeModProperties();
	}

	public override string ToString()
	{
		return Name;
	}
}
