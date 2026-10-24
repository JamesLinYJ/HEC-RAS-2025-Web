using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.GDALAssist;

namespace Ras.Observables;

public class ObservableGdalRasterSummary : ObservableObject, INotifyPropertyChanged
{
	public ObservableCollection<DateTime> _overrideTimestamps;
	public bool _useBand;

	public Dictionary<DateTime, BandSummary> BandSummaries = new Dictionary<DateTime, BandSummary>();

	private GdalRasterSummary _rasterSummary;

	public string Filepath => _rasterSummary.Path;

	public string Filename => Path.GetFileName(_rasterSummary.Path);

	public int BandCount => _rasterSummary.BandCount;

	public Projection Projection => _rasterSummary.Projection;

	public string ConnectionString => _rasterSummary.ConnectionString;

	public Dictionary<string, string> FileMetadata => _rasterSummary.FileMetadata;

	public DateTime[] BandTimestamps => _rasterSummary.BandTimestamps;

	public string Units => _rasterSummary.Units;

	public DateTime[] ForecastIssueTimes => _rasterSummary.ForecastIssueTimes;

	public string DataType => _rasterSummary.DataType;

	public GdalRasterSummary RasterSummary => _rasterSummary;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<DateTime> OverrideTimestamps
	{
		get
		{
			return _overrideTimestamps;
		}
		[MemberNotNull("_overrideTimestamps")]
		set
		{
			if (!EqualityComparer<ObservableCollection<DateTime>>.Default.Equals(_overrideTimestamps, value))
			{
				OnPropertyChanging("OverrideTimestamps");
				_overrideTimestamps = value;
				OnPropertyChanged("OverrideTimestamps");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool UseBand
	{
		get
		{
			return _useBand;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_useBand, value))
			{
				bool useBand = _useBand;
				OnPropertyChanging("UseBand");
				_useBand = value;
				OnUseBandChanged(useBand, value);
				OnPropertyChanged("UseBand");
			}
		}
	}

	public ObservableGdalRasterSummary(GdalRasterSummary grs)
	{
		_rasterSummary = grs;
		OverrideTimestamps = new ObservableCollection<DateTime>(grs.BandTimestamps);
		UseBand = true;
		for (int i = 0; i < BandCount; i++)
		{
			BandSummaries[BandTimestamps[i]] = new BandSummary(i + 1, BandTimestamps[i], _use: true);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnUseBandChanged(bool oldValue, bool newValue)
	{
		foreach (BandSummary bandSummary in BandSummaries.Values)
		{
			if (!newValue)
			{
				bandSummary.UseBand = newValue;
			}
		}
	}
}
