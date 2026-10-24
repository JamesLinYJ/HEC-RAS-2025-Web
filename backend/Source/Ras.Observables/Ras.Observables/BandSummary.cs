using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ras.Observables;

public class BandSummary : ObservableObject
{
	private int _bandIndex;
	private DateTime _bandTime;
	private bool _useBand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int BandIndex
	{
		get
		{
			return _bandIndex;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_bandIndex, value))
			{
				OnPropertyChanging("BandIndex");
				_bandIndex = value;
				OnPropertyChanged("BandIndex");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime BandTime
	{
		get
		{
			return _bandTime;
		}
		set
		{
			if (!EqualityComparer<DateTime>.Default.Equals(_bandTime, value))
			{
				OnPropertyChanging("BandTime");
				_bandTime = value;
				OnPropertyChanged("BandTime");
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
				OnPropertyChanging("UseBand");
				_useBand = value;
				OnPropertyChanged("UseBand");
			}
		}
	}

	public BandSummary(int _bandIdx, DateTime _bandTm, bool _use)
	{
		BandIndex = _bandIdx;
		BandTime = _bandTm;
		UseBand = _use;
	}
}
