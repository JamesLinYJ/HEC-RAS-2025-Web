using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.IO.ORM;
using H5Assist;
using Ras.Layers.Meteorology;
using Utility;

namespace Ras.Layers;

[DebuggerDisplay("{Count} Stations")]
public class MeteorologicStationLayer : IList<MeteorologicStation>, ICollection<MeteorologicStation>, IEnumerable<MeteorologicStation>, IEnumerable
{
	public enum SanitizationBehavior
	{
		Rename,
		Remove,
		ThrowException
	}

	public const string HDFGroupName = "/Boundary Conditions/Meteorology/Meteorology Stations";

	public const string GaugeCorrectSpelling = "Gauge";

	private List<MeteorologicStation> _stations = new List<MeteorologicStation>();

	public BoundaryCondition BoundaryConditions { get; }

	public IReadOnlyList<MeteorologicStation> Stations => _stations;

	public bool HasLoaded => _stations != null;

	public int Count => ((ICollection<MeteorologicStation>)_stations).Count;

	public bool IsReadOnly => ((ICollection<MeteorologicStation>)_stations).IsReadOnly;

	public MeteorologicStation this[int index]
	{
		get
		{
			return _stations[index];
		}
		set
		{
			MeteorologicStation previous = _stations[index];
			_stations[index] = value;
			if (!ReferenceEquals(previous, value)) BoundaryConditions.NotifyMetStationRemoved(previous);
		}
	}

	internal MeteorologicStationLayer(BoundaryCondition bc)
	{
		BoundaryConditions = bc;
	}

	public void Save(H5Writer hw)
	{
		if (hw.ExistsAsGroup("/Boundary Conditions/Meteorology/Meteorology Stations"))
		{
			hw.Delete("/Boundary Conditions/Meteorology/Meteorology Stations");
		}
		EnsureStationNamesSanitized(SanitizationBehavior.Rename);
		FeatureIO.Write(hw, "/Boundary Conditions/Meteorology/Meteorology Stations", _stations);
	}

	public void Load(H5Reader hr)
	{
		_stations = FeatureIO.Read<MeteorologicStation>(hr, "/Boundary Conditions/Meteorology/Meteorology Stations");
	}

	public void ReconcileLoadedGauges(IList<MetGaugedData> data)
	{
		for (int i = 0; i < data.Count; i++)
		{
			MetGaugedData metGaugedData = data[i];
			string assignedName = metGaugedData.AssignedName;
			if (string.IsNullOrEmpty(assignedName))
			{
				metGaugedData.Station = null;
				continue;
			}
			metGaugedData.Station = GetStationByName(assignedName);
			if (metGaugedData.Station == null)
			{
				throw new Exception("Loading error, we don't want to save references to non-existent gauges.");
			}
		}
	}

	public void Add(MeteorologicStation meteorologicStation)
	{
		_stations.Add(meteorologicStation);
	}

	public void Remove(MeteorologicStation station)
	{
		if (_stations.Remove(station))
		{
			BoundaryConditions.NotifyMetStationRemoved(station);
			return;
		}
		throw new Exception("Error - tried to remove a station that we don't own.");
	}

	public MeteorologicStation GetStationByName(string name)
	{
		foreach (MeteorologicStation station in _stations)
		{
			if (station.Name == name)
			{
				return station;
			}
		}
		return null;
	}

	public void EnsureStationNamesSanitized(SanitizationBehavior behavior)
	{
		switch (behavior)
		{
		case SanitizationBehavior.Rename:
			StringHelpers.EnsureUniqueNames(_stations, (MeteorologicStation st) => st.Name, (MeteorologicStation st, string n) =>
			{
				st.Name = n;
			}, "Station");
			break;
		case SanitizationBehavior.ThrowException:
			StringHelpers.EnsureUniqueNames(_stations, (MeteorologicStation st) => st.Name, (MeteorologicStation st, string n) =>
			{
				throw new Exception("Stations had the same name.");
			}, "Station");
			break;
		case SanitizationBehavior.Remove:
			throw new NotImplementedException();
		default:
			throw new NotSupportedException();
		}
	}

	public int IndexOf(MeteorologicStation meteorologicStation)
	{
		return ((IList<MeteorologicStation>)_stations).IndexOf(meteorologicStation);
	}

	public void Insert(int index, MeteorologicStation meteorologicStation)
	{
		((IList<MeteorologicStation>)_stations).Insert(index, meteorologicStation);
	}

	public void RemoveAt(int index)
	{
		Remove(_stations[index]);
	}

	public void Clear()
	{
		while (_stations.Count > 0) Remove(_stations[_stations.Count - 1]);
	}

	public bool Contains(MeteorologicStation meteorologicStation)
	{
		return ((ICollection<MeteorologicStation>)_stations).Contains(meteorologicStation);
	}

	public void CopyTo(MeteorologicStation[] stations, int arrayIndex)
	{
		((ICollection<MeteorologicStation>)_stations).CopyTo(stations, arrayIndex);
	}

	bool ICollection<MeteorologicStation>.Remove(MeteorologicStation meteorologicStation)
	{
		if (!_stations.Contains(meteorologicStation)) return false;
		Remove(meteorologicStation);
		return true;
	}

	public IEnumerator<MeteorologicStation> GetEnumerator()
	{
		return ((IEnumerable<MeteorologicStation>)_stations).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable)_stations).GetEnumerator();
	}
}
