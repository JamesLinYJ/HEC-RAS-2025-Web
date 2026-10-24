using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Utility;

namespace Geospatial.Rendering;

public class ProfileState : IProfileState
{
	public delegate void ProfileChangedEvent(int newPfIdx);

	public delegate void ProfileStructureChangedEvent();

	public const int MinProfileIndex = int.MinValue;

	public const int MaxProfileIndex = int.MaxValue;

	public const int NullProfileIndex = -1;

	private long _version = 1L;

	private readonly object _pfNameLock;

	private readonly Func<List<string>> _jitAllocator;

	private List<string> _pfNames;

	private int _curIdx;

	public long Version => _version;

	public IReadOnlyList<string> ProfileNames => Ensure();

	public int ProfileCount => Ensure().Count;

	public bool CanSetMin { get; set; }

	public bool CanSetMax { get; set; }

	public bool CanSetNull { get; set; } = true;

	public bool IsNull => _curIdx == -1;

	public bool IsMax => _curIdx == int.MaxValue;

	public bool IsMin => _curIdx == int.MinValue;

	public bool IsRegularProfile => IsRegularProfileIndex(_curIdx);

	public int CurrentProfileIndex
	{
		get
		{
			return _curIdx;
		}
		set
		{
			if (_curIdx != value)
			{
				if (IsRegularProfileIndex(value))
				{
					_curIdx = value;
				}
				else if (value == -1 && CanSetNull)
				{
					_curIdx = -1;
				}
				else if (value == int.MinValue && CanSetMin)
				{
					_curIdx = int.MinValue;
				}
				else if (value == int.MaxValue && CanSetMax)
				{
					_curIdx = int.MaxValue;
				}
				else
				{
					_curIdx = 0;
				}
				Interlocked.Increment(ref _version);
				OnProfileIndexChanged(_curIdx);
			}
		}
	}

	public string CurrentProfileName
	{
		get
		{
			int curIdx = _curIdx;
			if (IsRegularProfileIndex(curIdx))
			{
				return ProfileNames[curIdx];
			}
			return curIdx switch
			{
				int.MinValue => "Min", 
				int.MaxValue => "Max", 
				_ => "None", 
			};
		}
	}

	public event ProfileChangedEvent ProfileChanged;

	public event ProfileStructureChangedEvent ProfileStructureChanged;

	public bool IsRegularProfileIndex(int idx)
	{
		if (idx >= 0)
		{
			return idx < ProfileCount;
		}
		return false;
	}

	public ProfileState(IList<string> pfNames)
	{
		_pfNames = pfNames.ToList();
	}

	public ProfileState(Func<List<string>> jitProfileNameAlloc)
	{
		_pfNameLock = new object();
		_jitAllocator = jitProfileNameAlloc;
	}

	public void SetToLastProfile()
	{
		CurrentProfileIndex = ProfileCount - 1;
	}

	private void OnProfileIndexChanged(int newPf)
	{
		ProfileChanged?.Invoke(newPf);
	}

	public void AddProfile(string pfName)
	{
		Ensure().Add(pfName);
		ProfileStructureChanged?.Invoke();
	}

	public void Link(ProfileState other)
	{
	}

	private List<string> Ensure()
	{
		return LazyThreadsafe<List<string>>.AllocateOnce(ref _pfNames, _pfNameLock, _jitAllocator);
	}
}
