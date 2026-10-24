using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Geospatial.Rendering;

public class HillshadeProperties : INotifyPropertyChanged
{
	private long _version;

	public const bool IsEnabledDefault = false;

	public const float AzimuthDefault = 315f;

	public const float ZenithDefault = 45f;

	public const float ZFactorDefault = 3f;

	private bool _isEnabled;

	private float _zFactor = 3f;

	private float _azimuth = 315f;

	private float _zenith = 45f;

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
				IncrementVersion();
				NotifyPropertyChanged("IsEnabled");
			}
		}
	}

	public float ZFactor
	{
		get
		{
			return _zFactor;
		}
		set
		{
			if (_zFactor != value)
			{
				_zFactor = value;
				IncrementVersion();
				NotifyPropertyChanged("ZFactor");
			}
		}
	}

	public float Azimuth
	{
		get
		{
			return _azimuth;
		}
		set
		{
			if (_azimuth != value)
			{
				_azimuth = value;
				IncrementVersion();
				NotifyPropertyChanged("Azimuth");
			}
		}
	}

	public float Zenith
	{
		get
		{
			return _zenith;
		}
		set
		{
			if (_zenith != value)
			{
				_zenith = value;
				IncrementVersion();
				NotifyPropertyChanged("Zenith");
			}
		}
	}

	public event PropertyChangedEventHandler PropertyChanged;

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

	public HillshadeProperties()
	{
	}

	public HillshadeProperties(bool enabled, float zFactor, float azimuth, float zenith)
	{
		IsEnabled = enabled;
		ZFactor = zFactor;
		Azimuth = azimuth;
		Zenith = zenith;
	}

	public HillshadeProperties Clone()
	{
		return new HillshadeProperties(IsEnabled, ZFactor, Azimuth, Zenith);
	}

	protected void IncrementVersion()
	{
		Interlocked.Increment(ref _version);
	}

	public long GetVersion()
	{
		return _version;
	}
}
