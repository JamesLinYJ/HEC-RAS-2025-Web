using System;
using System.Collections.Generic;
using System.Linq;

namespace RasMapperLib;

public class AnimationContext : IAnimatingLayer
{
	public const int MaxProfileIndex = int.MaxValue;

	public const int MinProfileIndex = int.MinValue;

	public const int NullProfileIndex = -1;

	public const string MaxProfileName = "Max";

	public const string MinProfileName = "Min";

	public const string NullProfileName = "None";

	private int _curPfIdx;

	private List<string> _profiles;

	public bool CanProvideMinMax { get; }

	public bool CanAnimate => ProfileCount > 0;

	public bool IsProfile
	{
		get
		{
			if (_curPfIdx >= 0)
			{
				return _curPfIdx < ProfileCount;
			}
			return false;
		}
	}

	public bool IsMinProfile => _curPfIdx == int.MinValue;

	public bool IsMaxProfile => _curPfIdx == int.MaxValue;

	public bool IsNullProfile
	{
		get
		{
			if (!IsProfile && !IsMinProfile)
			{
				return !IsMaxProfile;
			}
			return false;
		}
	}

	public int CurrentProfileIndex
	{
		get
		{
			return _curPfIdx;
		}
		set
		{
			if (CanProvideMinMax && (value == int.MinValue || value == int.MaxValue))
			{
				_curPfIdx = value;
			}
			else if (value >= 0 && value < ProfileCount)
			{
				_curPfIdx = value;
			}
			else
			{
				_curPfIdx = -1;
			}
		}
	}

	public string CurrentProfileName
	{
		get
		{
			if (IsProfile)
			{
				return AvailableProfiles[CurrentProfileIndex];
			}
			if (IsMaxProfile)
			{
				return "Max";
			}
			if (IsMinProfile)
			{
				return "Min";
			}
			return "None";
		}
	}

	public IList<string> AvailableProfiles => _profiles;

	public int ProfileCount => AvailableProfiles.Count;

	public AnimationContext(IList<string> profiles, bool allowMinMax, int startingProfile = 0)
	{
		_profiles = profiles.ToList();
		CanProvideMinMax = allowMinMax;
	}

	public AnimationContext(Func<IList<string>> getProfiles)
	{
	}

	public void TrySetProfile(string profile)
	{
		throw new NotImplementedException();
	}

	void IAnimatingLayer.TrySetProfile(string profile)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profile);
	}

	public void SetToMaxProfile()
	{
		if (CanProvideMinMax)
		{
			CurrentProfileIndex = int.MaxValue;
		}
		else
		{
			CurrentProfileIndex = -1;
		}
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
		if (CanProvideMinMax)
		{
			CurrentProfileIndex = int.MinValue;
		}
		else
		{
			CurrentProfileIndex = -1;
		}
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}
}
