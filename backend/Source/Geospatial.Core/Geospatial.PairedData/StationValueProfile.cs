using System;
using System.Collections.Generic;
using System.Linq;
using Utility;
using Utility.Hashing;
using Utility.Unsafe;

namespace Geospatial.PairedData;

public class StationValueProfile<T> : IProfile<T>, IDataHash, IMutableSpatialVersion, ISpatialVersion where T : unmanaged, IEquatable<T>
{
	protected readonly List<double> _stations;

	protected readonly List<T> _values;

	public int Count => _stations.Count;

	public IReadOnlyList<double> Stations => _stations;

	public IReadOnlyList<T> Values => _values;

	public double MinStation
	{
		get
		{
			if (_stations.Count > 0)
			{
				return _stations[0];
			}
			return double.NaN;
		}
	}

	public double MaxStation
	{
		get
		{
			if (_stations.Count > 0)
			{
				List<double> stations = _stations;
				return stations[stations.Count - 1];
			}
			return double.NaN;
		}
	}

	public double StationLength => MaxStation - MinStation;

	public ExtrapolationBehavior ExtrapolationBehavior { get; set; } = ExtrapolationBehavior.ReturnZero;

	public long VersionXY { get; private set; } = 1L;

	public Memory<double> GetStationBufferUnsafe()
	{
		return UnsafeHelpers.GetListBackingBuffer(_stations);
	}

	public Memory<T> GetValueBufferUnsafe()
	{
		return UnsafeHelpers.GetListBackingBuffer(_values);
	}

	public StationValueProfile()
	{
		_stations = new List<double>();
		_values = new List<T>();
	}

	public StationValueProfile(int capacity)
	{
		_stations = new List<double>(capacity);
		_values = new List<T>(capacity);
	}

	public StationValueProfile(IEnumerable<double> stations, IEnumerable<T> values)
	{
		if (stations == null || values == null)
		{
			throw new ArgumentNullException("stations/values");
		}
		_stations = stations.ToList();
		_values = values.ToList();
		if (_stations.Count != _values.Count)
		{
			throw new ArgumentException("Station/Value arrays are of different lengths.");
		}
	}

	private StationValueProfile(List<double> stations, List<T> values, bool fakeArgForInternalConstruction)
	{
		_stations = stations ?? throw new ArgumentNullException("stations");
		_values = values ?? throw new ArgumentNullException("values");
		if (_stations.Count != _values.Count)
		{
			throw new ArgumentException("Station/Value arrays are of different lengths.");
		}
	}

	public static StationValueProfile<K> FromBuffers<K>(List<double> stations, List<K> values) where K : unmanaged, IEquatable<K>
	{
		return new StationValueProfile<K>(stations, values, fakeArgForInternalConstruction: true);
	}

	public void SortByStation()
	{
		if (!IsStationSorted())
		{
			int[] quickSortMap = MathHelpers.GetQuickSortMap(_stations);
			double[] doubleValues = new double[quickSortMap.Length];
			T[] values = new T[quickSortMap.Length];
			for (int i = 0; i < quickSortMap.Length; i++)
			{
				doubleValues[i] = _stations[quickSortMap[i]];
				values[i] = _values[quickSortMap[i]];
			}
			for (int j = 0; j < quickSortMap.Length; j++)
			{
				_stations[j] = doubleValues[j];
				_values[j] = values[j];
			}
		}
	}

	public bool IsStationSorted()
	{
		if (_stations.Count == 0)
		{
			return true;
		}
		double station = _stations[0];
		for (int i = 1; i < _stations.Count; i++)
		{
			double station2 = _stations[i];
			if (station2 < station)
			{
				return false;
			}
			station = station2;
		}
		return true;
	}

	public bool HasDuplicates()
	{
		if (!IsStationSorted())
		{
			return false;
		}
		for (int i = 1; i < _stations.Count; i++)
		{
			if (_stations[i - 1] == _stations[i])
			{
				return true;
			}
		}
		return false;
	}

	public void AdjustStationing(double dx)
	{
		for (int i = 0; i < Count; i++)
		{
			_stations[i] += dx;
		}
	}

	public void AdjustToZeroStationing()
	{
		if (Count != 0)
		{
			double station = _stations[0];
			if (station != 0.0)
			{
				AdjustStationing(-1.0 * station);
			}
		}
	}

	public void Add(double station, T localValue)
	{
		_stations.Add(station);
		_values.Add(localValue);
		VersionXY++;
	}

	public void Insert(double station, T localValue)
	{
		if (Count == 0)
		{
			_stations.Add(station);
			_values.Add(localValue);
			return;
		}
		int binarySearch = MathHelpers.BinarySearch(_stations, station);
		if (binarySearch >= 0)
		{
			_stations.Insert(binarySearch, station);
			_values.Insert(binarySearch, localValue);
		}
		else
		{
			binarySearch = ~binarySearch;
			if (binarySearch == Count)
			{
				_stations.Add(station);
				_values.Add(localValue);
			}
			else
			{
				_stations.Insert(binarySearch, station);
				_values.Insert(binarySearch, localValue);
			}
		}
		VersionXY++;
	}

	public void InsertAtIndex(int index, double station, T atIndex)
	{
		_stations.Insert(index, station);
		_values.Insert(index, atIndex);
		VersionXY++;
	}

	public void Clear()
	{
		_stations.Clear();
		_values.Clear();
		VersionXY++;
	}

	public void AddRange(IEnumerable<double> stations, IEnumerable<T> values)
	{
		_stations.AddRange(stations);
		_values.AddRange(values);
		if (_stations.Count != _values.Count)
		{
			throw new Exception("Station/Value length mismatch.");
		}
		VersionXY++;
	}

	public void AddRange(StationValueProfile<T> other)
	{
		_stations.AddRange(other._stations);
		_values.AddRange(other._values);
		VersionXY++;
	}

	public void AddRange(StationValueProfile<T> other, int start, int count)
	{
		for (int i = start; i < start + count; i++)
		{
			_stations.Add(other._stations[i]);
			_values.Add(other._values[i]);
		}
		VersionXY++;
	}

	public void GetRange(int start, int count, out double[] stations, out T[] values)
	{
		if (start < 0 || count < 0)
		{
			throw new ArgumentOutOfRangeException();
		}
		if (start + count >= Count)
		{
			throw new ArgumentOutOfRangeException();
		}
		stations = _stations.GetRange(start, count).ToArray();
		values = _values.GetRange(start, count).ToArray();
	}

	public void Remove(int index)
	{
		_stations.RemoveAt(index);
		_values.RemoveAt(index);
		VersionXY++;
	}

	public void SetStation(int index, double station)
	{
		_stations[index] = station;
		VersionXY++;
	}

	public void SetValue(int index, T assignedValue)
	{
		_values[index] = assignedValue;
		VersionXY++;
	}

	public virtual T Sample(double station)
	{
		int binarySearch = MathHelpers.BinarySearch(_stations, station);
		if (binarySearch >= 0)
		{
			return _values[binarySearch];
		}
		binarySearch = ~binarySearch;
		if (binarySearch == 0 || binarySearch == Count)
		{
			if (binarySearch == Count)
			{
				binarySearch = Count - 1;
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.ReturnNoData)
			{
				return default;
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.ReturnZero)
			{
				return default;
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.NearestValue)
			{
				return _values[binarySearch];
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.ExtrapolateNearestSegment)
			{
				return _values[binarySearch];
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.ThrowException)
			{
				throw new ArgumentException("Station was out of range.");
			}
			throw new NotImplementedException();
		}
		return _values[binarySearch - 1];
	}

	public virtual StationValueProfile<T> Clone()
	{
		return new StationValueProfile<T>(_stations, _values);
	}

	public int GetDataHash()
	{
		int combine = 0;
		for (int i = 0; i < _stations.Count; i++)
		{
			combine = HashCode.Combine(combine, _stations[i]);
		}
		for (int j = 0; j < _values.Count; j++)
		{
			combine = HashCode.Combine(combine, _values[j]);
		}
		return combine;
	}

	public void FreezeOne()
	{
	}

	public void ThawOne()
	{
	}

	public void ForceSetVersion(long versionXY)
	{
		VersionXY = versionXY;
	}
}
