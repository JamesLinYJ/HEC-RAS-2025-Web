using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Geospatial.Rendering;

public class AggregatedProfileState : IProfileState
{
	private readonly List<IProfileState> _children;

	private readonly List<DateTime> _unionTimestamps;

	private readonly List<string> _profileNames;

	private int _currentProfileIndex = -1;

	private long _version = 1L;

	public IReadOnlyList<IProfileState> Children => _children;

	public int CurrentProfileIndex
	{
		get
		{
			return _currentProfileIndex;
		}
		set
		{
			if (_currentProfileIndex == value)
			{
				return;
			}
			_currentProfileIndex = value;
			if (value >= 0 && value < _unionTimestamps.Count)
			{
				DateTime targetTimestamp = _unionTimestamps[value];
				for (int i = 0; i < _children.Count; i++)
				{
					int currentProfileIndex = FindNearestTimestampIndex(_children[i], targetTimestamp);
					_children[i].CurrentProfileIndex = currentProfileIndex;
				}
			}
			else if (value < 0)
			{
				for (int j = 0; j < _children.Count; j++)
				{
					_children[j].CurrentProfileIndex = value;
				}
			}
			Interlocked.Increment(ref _version);
			ProfileChanged?.Invoke(value);
		}
	}

	public int ProfileCount => _unionTimestamps.Count;

	public long Version => _version;

	public bool IsRegularProfile
	{
		get
		{
			if (_currentProfileIndex >= 0)
			{
				return _currentProfileIndex < _unionTimestamps.Count;
			}
			return false;
		}
	}

	public bool IsNull => _currentProfileIndex == -1;

	public IReadOnlyList<string> ProfileNames => _profileNames;

	public event ProfileState.ProfileChangedEvent ProfileChanged;

	public event ProfileState.ProfileStructureChangedEvent ProfileStructureChanged;

	public AggregatedProfileState(IEnumerable<IProfileState> children)
	{
		_children = children?.ToList() ?? throw new ArgumentNullException("children");
		if (_children.Count == 0)
		{
			throw new ArgumentException("Must provide at least one child profile state.", "children");
		}
		_unionTimestamps = new List<DateTime>();
		_profileNames = new List<string>();
		RebuildUnionTimestampsFromChildren("children");
		foreach (IProfileState child in _children)
		{
			child.ProfileChanged += OnChildProfileChanged;
			child.ProfileStructureChanged += OnChildProfileStructureChanged;
		}
	}

	public static int FindNearestTimestampIndex(IProfileState child, DateTime targetTimestamp)
	{
		if (child.ProfileCount == 0)
		{
			return -1;
		}
		long ticks = targetTimestamp.Ticks;
		int result = 0;
		long maxValue = long.MaxValue;
		for (int i = 0; i < child.ProfileCount; i++)
		{
			if (DateTime.TryParse(child.ProfileNames[i], out var result2))
			{
				long maxValue2 = Math.Abs(result2.Ticks - ticks);
				if (maxValue2 < maxValue)
				{
					maxValue = maxValue2;
					result = i;
				}
			}
		}
		return result;
	}

	private void OnChildProfileChanged(int newPfIdx)
	{
	}

	private void OnChildProfileStructureChanged()
	{
		int currentProfileIndex = _currentProfileIndex;
		DateTime? dateTime = null;
		if (currentProfileIndex >= 0 && currentProfileIndex < _unionTimestamps.Count)
		{
			dateTime = _unionTimestamps[currentProfileIndex];
		}
		RebuildUnionTimestampsFromChildren();
		if (_unionTimestamps.Count == 0)
		{
			CurrentProfileIndex = -1;
			ProfileStructureChanged?.Invoke();
			return;
		}
		if (currentProfileIndex < 0)
		{
			CurrentProfileIndex = currentProfileIndex;
		}
		else if (dateTime.HasValue)
		{
			int currentProfileIndex2 = FindUnionTimestampIndex(dateTime.Value);
			if (currentProfileIndex2 == -1)
			{
				currentProfileIndex2 = FindNearestUnionTimestampIndex(dateTime.Value);
			}
			if (currentProfileIndex2 >= 0)
			{
				CurrentProfileIndex = currentProfileIndex2;
			}
		}
		else if (currentProfileIndex >= _unionTimestamps.Count && _unionTimestamps.Count > 0)
		{
			CurrentProfileIndex = _unionTimestamps.Count - 1;
		}
		ProfileStructureChanged?.Invoke();
	}

	private void RebuildUnionTimestampsFromChildren(string argumentExceptionParamName = null)
	{
		SortedSet<DateTime> sortedSet = new SortedSet<DateTime>();
		foreach (IProfileState child in _children)
		{
			foreach (string profileName in child.ProfileNames)
			{
				if (!DateTime.TryParse(profileName, out var result))
				{
					string message = "All profile names must be DateTime-parseable for aggregation. Found non-parseable name: '" + profileName + "'";
					if (!string.IsNullOrEmpty(argumentExceptionParamName))
					{
						throw new ArgumentException(message, argumentExceptionParamName);
					}
					throw new InvalidOperationException(message);
				}
				sortedSet.Add(result);
			}
		}
		_unionTimestamps.Clear();
		_unionTimestamps.AddRange(sortedSet);
		_profileNames.Clear();
		_profileNames.AddRange(_unionTimestamps.Select((DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm:ss")));
	}

	private int FindUnionTimestampIndex(DateTime timestamp)
	{
		int binarySearch = _unionTimestamps.BinarySearch(timestamp);
		if (binarySearch < 0)
		{
			return -1;
		}
		return binarySearch;
	}

	private int FindNearestUnionTimestampIndex(DateTime targetTimestamp)
	{
		if (_unionTimestamps.Count == 0)
		{
			return -1;
		}
		int binarySearch = _unionTimestamps.BinarySearch(targetTimestamp);
		if (binarySearch >= 0)
		{
			return binarySearch;
		}
		int binarySearchTerm = ~binarySearch;
		if (binarySearchTerm <= 0)
		{
			return 0;
		}
		if (binarySearchTerm >= _unionTimestamps.Count)
		{
			return _unionTimestamps.Count - 1;
		}
		DateTime dateTime = _unionTimestamps[binarySearchTerm - 1];
		DateTime dateTime2 = _unionTimestamps[binarySearchTerm];
		long absoluteTicks = Math.Abs((targetTimestamp - dateTime).Ticks);
		long absoluteTicks2 = Math.Abs((dateTime2 - targetTimestamp).Ticks);
		if (absoluteTicks > absoluteTicks2)
		{
			return binarySearchTerm;
		}
		return binarySearchTerm - 1;
	}
}
