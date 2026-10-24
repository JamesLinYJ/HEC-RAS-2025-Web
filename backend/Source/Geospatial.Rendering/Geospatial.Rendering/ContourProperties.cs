using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Geospatial.Rendering;

public class ContourProperties : INotifyPropertyChanged, IMutableSpatialVersion, ISpatialVersion
{
	private long _version = 1L;

	public const bool IsEnabledDefault = false;

	public const bool AutoIntervalDefault = true;

	public const float ContourIntervalDefault = 10f;

	public const int IndexEveryDefault = 5;

	public const int ColorArgbDefault = -16777216;

	public static readonly Color ColorDefault = Color.FromArgb(-16777216);

	public const int OpacityDefault = 70;

	public const bool ShowLabelsDefault = true;

	public const float DensityDefault = 1f;

	public const float DensityMin = 0.025f;

	public const float DensityMax = 4f;

	private bool _isEnabled;

	private bool _autoInterval = true;

	private float _contourInterval = 10f;

	private int _indexEvery = 5;

	private Color _color = ColorDefault;

	private int _opacity = 70;

	private float _density = 1f;

	private string _unitLabel = string.Empty;

	private bool _showLabels = true;

	public long VersionXY => _version;

	public bool IsEnabled
	{
		get
		{
			return _isEnabled;
		}
		set
		{
			if (_isEnabled != value)
			{
				_isEnabled = value;
				Interlocked.Increment(ref _version);
				NotifyPropertyChanged("IsEnabled");
			}
		}
	}

	public bool AutoInterval
	{
		get
		{
			return _autoInterval;
		}
		set
		{
			if (_autoInterval != value)
			{
				_autoInterval = value;
				Interlocked.Increment(ref _version);
				NotifyPropertyChanged("AutoInterval");
			}
		}
	}

	public float ContourInterval
	{
		get
		{
			return _contourInterval;
		}
		set
		{
			if (!(value <= 0f) && _contourInterval != value)
			{
				_contourInterval = value;
				Interlocked.Increment(ref _version);
				NotifyPropertyChanged("ContourInterval");
			}
		}
	}

	public int IndexEvery
	{
		get
		{
			return _indexEvery;
		}
		set
		{
			if (value >= 0 && _indexEvery != value)
			{
				_indexEvery = value;
				Interlocked.Increment(ref _version);
				NotifyPropertyChanged("IndexEvery");
			}
		}
	}

	public Color Color
	{
		get
		{
			return _color;
		}
		set
		{
			Color color = Color.FromArgb(255, value.R, value.G, value.B);
			if (_color.ToArgb() != color.ToArgb())
			{
				_color = color;
				Interlocked.Increment(ref _version);
				NotifyPropertyChanged("Color");
			}
		}
	}

	public int Opacity
	{
		get
		{
			return _opacity;
		}
		set
		{
			int opacity = Math.Clamp(value, 0, 100);
			if (_opacity != opacity)
			{
				_opacity = opacity;
				Interlocked.Increment(ref _version);
				NotifyPropertyChanged("Opacity");
			}
		}
	}

	public float Density
	{
		get
		{
			return _density;
		}
		set
		{
			float density = Math.Clamp(value, 0.025f, 4f);
			if (_density != density)
			{
				_density = density;
				Interlocked.Increment(ref _version);
				NotifyPropertyChanged("Density");
			}
		}
	}

	public string UnitLabel
	{
		get
		{
			return _unitLabel;
		}
		set
		{
			string unitLabel = value ?? string.Empty;
			if (!(_unitLabel == unitLabel))
			{
				_unitLabel = unitLabel;
				Interlocked.Increment(ref _version);
				NotifyPropertyChanged("UnitLabel");
			}
		}
	}

	public bool ShowLabels
	{
		get
		{
			return _showLabels;
		}
		set
		{
			if (_showLabels != value)
			{
				_showLabels = value;
				Interlocked.Increment(ref _version);
				NotifyPropertyChanged("ShowLabels");
			}
		}
	}

	public event PropertyChangedEventHandler PropertyChanged;

	public void ForceSetVersion(long version)
	{
		_version = version;
	}

	public void FreezeOne()
	{
	}

	public void ThawOne()
	{
	}

	protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	[Conditional("DEBUG")]
	[DebuggerStepThrough]
	public void VerifyPropertyName(string propertyName)
	{
		if (TypeDescriptor.GetProperties(this)[propertyName] == null)
		{
			throw new Exception("Invalid property name: " + propertyName);
		}
	}

	public ContourProperties Clone()
	{
		return new ContourProperties
		{
			IsEnabled = IsEnabled,
			AutoInterval = AutoInterval,
			ContourInterval = ContourInterval,
			IndexEvery = IndexEvery,
			Color = Color,
			Opacity = Opacity,
			Density = Density,
			ShowLabels = ShowLabels,
			UnitLabel = UnitLabel
		};
	}
}
