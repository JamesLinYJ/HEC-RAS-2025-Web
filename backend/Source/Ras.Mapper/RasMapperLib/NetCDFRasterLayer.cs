using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RasMapperLib;

public class NetCDFRasterLayer : InterpolatedLayer, IAnimatingLayer
{
	private int _currentIndex;

	private NetCDFDescription _fileDesc;

	public bool CanProvideMinMax { get; }

	public bool CanAnimate { get; }

	public bool IsMinProfile { get; }

	public bool IsMaxProfile { get; }

	public bool IsNullProfile => _currentIndex == -1;

	public bool IsProfile => _currentIndex != -1;

	public int CurrentProfileIndex
	{
		get
		{
			return _currentIndex;
		}
		set
		{
			_currentIndex = value;
		}
	}

	public string CurrentProfileName
	{
		get
		{
			EnsureFileInfo();
			if (_currentIndex == -1 || _currentIndex >= _fileDesc.DateTimes.Count())
			{
				return "None";
			}
			return AvailableProfiles[_currentIndex];
		}
	}

	public IList<string> AvailableProfiles
	{
		get
		{
			EnsureFileInfo();
			return _fileDesc.DateTimes.Select([SpecialName] (DateTime dt) => dt.ToString()).ToList();
		}
	}

	public NetCDFRasterLayer()
		: base("NetCDF Layer")
	{
		_currentIndex = -1;
		CanProvideMinMax = false;
		CanAnimate = true;
		IsMinProfile = false;
		IsMaxProfile = false;
	}

	public NetCDFRasterLayer(string filename)
		: base("NetCDF Layer", filename)
	{
		_currentIndex = -1;
		CanProvideMinMax = false;
		CanAnimate = true;
		IsMinProfile = false;
		IsMaxProfile = false;
	}

	public NetCDFRasterLayer(string layername, string filename)
		: base(layername, filename)
	{
		_currentIndex = -1;
		CanProvideMinMax = false;
		CanAnimate = true;
		IsMinProfile = false;
		IsMaxProfile = false;
	}

	private void EnsureFileInfo()
	{
		if (_fileDesc == null)
		{
			_fileDesc = new NetCDFDescription(SourceFilename);
		}
	}

	public override bool Resample(RasterM raster, ref float[] values)
	{
		return base.Resample(raster, ref values);
	}

	public void TrySetProfile(string profile)
	{
		_currentIndex = -1;
	}

	void IAnimatingLayer.TrySetProfile(string profile)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profile);
	}

	public void SetToMaxProfile()
	{
		_currentIndex = -1;
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
		_currentIndex = -1;
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}
}
