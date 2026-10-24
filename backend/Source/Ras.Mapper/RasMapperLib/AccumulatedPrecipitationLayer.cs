using System;
using System.Collections.Generic;

namespace RasMapperLib;

public class AccumulatedPrecipitationLayer : Layer, IAnimatingLayer
{
	public IPrecipitationLayer ReferencePrecipLayer;

	protected int _currentBandIndex;

	public override Extent Extent => (ReferencePrecipLayer as Layer)?.Extent;

	public bool CanProvideMinMax { get; }

	public bool IsMinProfile { get; }

	public bool IsMaxProfile { get; }

	public bool CanAnimate { get; }

	public IList<string> AvailableProfiles => (ReferencePrecipLayer as IAnimatingLayer)?.AvailableProfiles;

	public int ProfileCount
	{
		get
		{
			if (AvailableProfiles == null)
			{
				return 0;
			}
			return AvailableProfiles.Count;
		}
	}

	public string CurrentProfileName
	{
		get
		{
			EnsureDataLoaded();
			if (IsNullProfile)
			{
				return "None";
			}
			return AvailableProfiles[_currentBandIndex];
		}
	}

	public bool IsProfile
	{
		get
		{
			if (_currentBandIndex >= 0)
			{
				return _currentBandIndex < ProfileCount;
			}
			return false;
		}
	}

	public bool IsNullProfile => !IsProfile;

	public int CurrentProfileIndex
	{
		get
		{
			return _currentBandIndex;
		}
		set
		{
			_currentBandIndex = value;
			OnCurrentBandChanged();
		}
	}

	public AccumulatedPrecipitationLayer(IPrecipitationLayer precipLayer)
		: base("Precipitation (Accumulated)")
	{
		_currentBandIndex = 0;
		CanProvideMinMax = true;
		IsMinProfile = false;
		IsMaxProfile = false;
		CanAnimate = true;
		ReferencePrecipLayer = precipLayer;
	}

	public void EnsureDataLoaded()
	{
	}

	private void RasterizeAccumulatedRatesInternal(RasterM raster, DateTime endTime, ref float[] rasterBuffer)
	{
	}

	protected virtual void OnCurrentBandChanged()
	{
	}

	public void TrySetProfile(string profile)
	{
		int currentProfileIndex = AvailableProfiles.IndexOf(profile);
		if (currentProfileIndex != -1)
		{
			CurrentProfileIndex = currentProfileIndex;
		}
	}

	void IAnimatingLayer.TrySetProfile(string profile)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profile);
	}

	public void SetToMaxProfile()
	{
		CurrentProfileIndex = checked(AvailableProfiles.Count - 1);
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
		CurrentProfileIndex = -1;
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}
}
