using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.PairedData;

namespace Visual.Mapping.Observables;

public class ObservableStationElevationProfile : ObservableObject
{
	public delegate void StationElevationProfileSortedEvent();

	public delegate void StationElevationProfileUpdatedEvent();

	private NotifyUpdateableDoubleCollection _stations;

	private NotifyUpdateableDoubleCollection _elevations;

	public StationElevationProfile Profile { get; private set; }

	public IReadOnlyList<double> Stations => _stations;

	public IReadOnlyList<double> Elevations => _elevations;
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSorted
	{
		get
		{
			return field;
		}
		private set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsSorted");
				field = value;
				OnPropertyChanged("IsSorted");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasEnoughData
	{
		get
		{
			return field;
		}
		private set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("HasEnoughData");
				field = value;
				OnPropertyChanged("HasEnoughData");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasDuplicates
	{
		get
		{
			return field;
		}
		private set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("HasDuplicates");
				field = value;
				OnPropertyChanged("HasDuplicates");
			}
		}
	}

	public int Count => Profile.Count;

	public event StationElevationProfileSortedEvent Sorted;

	public event StationElevationProfileUpdatedEvent DataChanged;

	public ObservableStationElevationProfile(StationElevationProfile profile)
	{
		Profile = profile;
		_stations = new NotifyUpdateableDoubleCollection(() => Profile.Stations);
		_elevations = new NotifyUpdateableDoubleCollection(() => Profile.Elevations);
		IsSorted = profile.IsStationSorted();
		HasEnoughData = Profile.Stations.Count >= 2;
		HasDuplicates = profile.HasDuplicates();
	}

	public void Add(double station, double elevation)
	{
		OnPropertyChanging("Count");
		Profile.Add(station, elevation);
		_stations.Update();
		_elevations.Update();
		OnPropertyChanged("Count");
		IsSorted = Profile.IsStationSorted();
		HasEnoughData = Profile.Stations.Count >= 2;
		HasDuplicates = Profile.HasDuplicates();
		DataChanged?.Invoke();
	}

	public void Insert(double station, double elevation)
	{
		OnPropertyChanging("Count");
		Profile.Insert(station, elevation);
		_stations.Update();
		_elevations.Update();
		OnPropertyChanged("Count");
		IsSorted = Profile.IsStationSorted();
		HasEnoughData = Profile.Stations.Count >= 2;
		HasDuplicates = Profile.HasDuplicates();
		DataChanged?.Invoke();
	}

	public void AddRange(IEnumerable<double> stations, IEnumerable<double> values)
	{
		OnPropertyChanging("Count");
		Profile.AddRange(stations, values);
		_stations.Update();
		_elevations.Update();
		OnPropertyChanged("Count");
		IsSorted = Profile.IsStationSorted();
		HasEnoughData = Profile.Stations.Count >= 2;
		HasDuplicates = Profile.HasDuplicates();
		DataChanged?.Invoke();
	}

	public void SetStation(int index, double station)
	{
		Profile.SetStation(index, station);
		_stations.Update();
		IsSorted = Profile.IsStationSorted();
		HasDuplicates = Profile.HasDuplicates();
		DataChanged?.Invoke();
	}

	public void SetElevation(int index, double elevation)
	{
		Profile.SetValue(index, elevation);
		_elevations.Update();
		DataChanged?.Invoke();
	}

	public void Remove(int index)
	{
		OnPropertyChanging("Count");
		Profile.Remove(index);
		_stations.Update();
		_elevations.Update();
		OnPropertyChanged("Count");
		IsSorted = Profile.IsStationSorted();
		HasEnoughData = Profile.Stations.Count >= 2;
		HasDuplicates = Profile.HasDuplicates();
		DataChanged?.Invoke();
	}

	public double Query(double stationVal)
	{
		return Profile.Query(stationVal);
	}

	public void Sort()
	{
		Profile.SortByStation();
		_stations.Update();
		_elevations.Update();
		IsSorted = Profile.IsStationSorted();
		HasDuplicates = Profile.HasDuplicates();
		Sorted?.Invoke();
		DataChanged?.Invoke();
	}

	public void Clear()
	{
		OnPropertyChanging("Count");
		Profile.Clear();
		_stations.Update();
		_elevations.Update();
		OnPropertyChanged("Count");
		IsSorted = false;
		HasEnoughData = false;
		HasDuplicates = false;
		DataChanged?.Invoke();
	}
}
