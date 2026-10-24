using System.Linq;

namespace RasMapperLib;

public class InMemoryProfileInfo : IProfileInfo
{
	private float _stationOffset;

	private Profile _profile;

	public int Count
	{
		get
		{
			if (_profile == null)
			{
				return 0;
			}
			return _profile.Count;
		}
	}

	public float FirstStation
	{
		get
		{
			if (_profile == null || _profile.Count == 0)
			{
				return 0f;
			}
			return _profile.First().X + _stationOffset;
		}
	}

	public float LastStation
	{
		get
		{
			if (_profile == null || _profile.Count == 0)
			{
				return 0f;
			}
			return _profile.Last().X + _stationOffset;
		}
	}

	public bool ShouldAdjustToZeroStationing => true;

	public InMemoryProfileInfo(Profile pf)
	{
		_stationOffset = 0f;
		_profile = pf;
	}

	public void SetStationingOffset(float offsetValue)
	{
		_stationOffset = offsetValue;
	}

	void IProfileInfo.SetStationingOffset(float offsetValue)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetStationingOffset
		this.SetStationingOffset(offsetValue);
	}

	public Profile GetProfile()
	{
		if (_profile == null)
		{
			return null;
		}
		Profile profile = _profile.Clone();
		if (_stationOffset != 0f)
		{
			profile.AdjustStationing(_stationOffset);
		}
		return profile;
	}

	Profile IProfileInfo.GetProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetProfile
		return this.GetProfile();
	}

	public override string ToString()
	{
		return "(" + Count + " Points)";
	}
}
