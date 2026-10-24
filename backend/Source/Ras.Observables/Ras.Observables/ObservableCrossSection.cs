using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Hydraulics;
using Ras.Hydraulics.CrossSections;

namespace Ras.Observables;

public class ObservableCrossSection : ObservableObject, IObservableFeature<CrossSection>
{
	private readonly CrossSection _crossSection;

	public CrossSection CrossSection => _crossSection;

	public CrossSection BaseFeature => _crossSection;

	public string River => _crossSection.RiverName;

	public string Reach => _crossSection.ReachName;

	public string RiverStation => _crossSection.RiverStation;

	public FrictionMode FrictionMode
	{
		get
		{
			return _crossSection.FrictionMode;
		}
		set
		{
			SetProperty(_crossSection.FrictionMode, value, _crossSection, (CrossSection u, FrictionMode n) =>
			{
				u.FrictionMode = n;
			}, "FrictionMode");
		}
	}

	public string NodeName
	{
		get
		{
			return _crossSection.NodeName;
		}
		set
		{
			SetProperty(_crossSection.NodeName, value, _crossSection, (CrossSection u, string n) =>
			{
				u.NodeName = n;
			}, "NodeName");
		}
	}

	public string Description
	{
		get
		{
			return _crossSection.Description;
		}
		set
		{
			SetProperty(_crossSection.Description, value, _crossSection, (CrossSection u, string n) =>
			{
				u.Description = n;
			}, "Description");
		}
	}

	public StationElevationProfile OverrideStationElevation
	{
		get
		{
			return _crossSection.OverrideStationElevation;
		}
		set
		{
			SetProperty(_crossSection.OverrideStationElevation, value, _crossSection, (CrossSection u, StationElevationProfile n) =>
			{
				u.OverrideStationElevation = n;
			}, "OverrideStationElevation");
		}
	}

	public StationElevationProfile StationElevation
	{
		get
		{
			return _crossSection.StationElevation;
		}
		set
		{
			SetProperty(_crossSection.StationElevation, value, _crossSection, (CrossSection u, StationElevationProfile n) =>
			{
				u.StationElevation = n;
			}, "StationElevation");
		}
	}

	public BankProperties BankProperties
	{
		get
		{
			return _crossSection.BankProperties;
		}
		set
		{
			SetProperty(_crossSection.BankProperties, value, _crossSection, (CrossSection u, BankProperties n) =>
			{
				u.BankProperties = n;
			}, "BankProperties");
		}
	}

	public StationElevationProfile ComputationStationElevation => OverrideStationElevation ?? StationElevation;

	public Polyline Polyline
	{
		get
		{
			return _crossSection.Polyline;
		}
		set
		{
			SetProperty(_crossSection.Polyline, value, _crossSection, (CrossSection u, Polyline n) =>
			{
				u.Polyline = n;
			}, "Polyline");
		}
	}

	public ObservableCrossSection(CrossSection crossSection)
	{
		_crossSection = crossSection;
	}

	public void Filter(double tolerance)
	{
		long versionXY = _crossSection.Polyline.VersionXY;
		_crossSection.Polyline.Filter(tolerance);
		if (_crossSection.Polyline.VersionXY != versionXY)
		{
			OnPropertyChanged("Polyline");
		}
	}

	public void Translate(double x, double y)
	{
		_crossSection.Polyline.Translate(x, y);
		OnPropertyChanged("Polyline");
	}

	public override string ToString()
	{
		return _crossSection.ToString();
	}
}
