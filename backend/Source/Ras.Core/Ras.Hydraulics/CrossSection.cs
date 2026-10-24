using System.Diagnostics;
using Geospatial;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Hydraulics.CrossSections;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics;

[DebuggerDisplay("{ToString()}")]
public class CrossSection : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	private Polyline _polyline;

	private VersionTracker _tracker;

	public long VersionXY => _tracker.GetVersion(_polyline);

	[HashContributor]
	[UserSettable(true)]
	[DatasetName("Polyline")]
	public Polyline Polyline
	{
		get
		{
			return _polyline;
		}
		set
		{
			_tracker.Assign(ref _polyline, value);
		}
	}

	public Extent Extent => Polyline?.Extent ?? Extent.NullExtent;

	[HashContributor]
	[UserSettable(false)]
	public StationElevationProfile StationElevation { get; set; }

	[HashContributor]
	[UserSettable(true)]
	[DatasetName("Station Elevation", Optional = true)]
	public StationElevationProfile OverrideStationElevation { get; set; }

	[HashContributor]
	[DatasetName("Manning's n", Optional = true)]
	public StationElevationProfile ManningsN { get; set; }

	[HashContributor]
	[UserSettable(false)]
	[Metadata("River")]
	public string RiverName { get; set; }

	[HashContributor]
	[UserSettable(false)]
	[Metadata("Reach")]
	public string ReachName { get; set; }

	[HashContributor]
	[UserSettable(false)]
	[Metadata("RS")]
	public string RiverStation { get; set; }

	[HashContributor]
	[UserSettable(true)]
	[Metadata("Node Name", Optional = true)]
	public string NodeName { get; set; }

	[HashContributor]
	[UserSettable(true)]
	[DatasetName("Description")]
	public string Description { get; set; }

	[HashContributor]
	[UserSettable(true)]
	[Metadata("Friction Mode")]
	public FrictionMode FrictionMode { get; set; }

	[HashContributor]
	[ComplexMetadata]
	public ReachLengths ReachLengths { get; set; }

	[HashContributor]
	[ComplexMetadata]
	public BankProperties BankProperties { get; set; }

	[HashContributor]
	[Metadata("Minor Loss", Optional = true)]
	public double MinorLoss { get; set; }

	[HashContributor]
	[Metadata("Skew", Optional = true)]
	public double Skew { get; set; }

	[HashContributor]
	[ComplexMetadata]
	public LeveeProperties LeveeProperties { get; set; }

	[HashContributor]
	[ComplexMetadata]
	public ContractionExpansionCoefficients ContractionExpansionCoefficients { get; set; }

	[HashContributor]
	[ComplexMetadata]
	public Ras.Hydraulics.CrossSections.HydraulicProperties HydraulicProperties { get; set; }

	[HashContributor]
	[ComplexMetadata(Optional = true)]
	public IceProperties IceProperties { get; set; }

	public CrossSection()
	{
		Init();
	}

	public CrossSection(Polyline xsPolyline)
	{
		_polyline = xsPolyline;
		Init();
	}

	private void Init()
	{
		ReachLengths = new ReachLengths();
		BankProperties = new BankProperties(() => Polyline);
		HydraulicProperties = new Ras.Hydraulics.CrossSections.HydraulicProperties();
		IceProperties = new IceProperties();
		ContractionExpansionCoefficients = new ContractionExpansionCoefficients();
		LeveeProperties = new LeveeProperties();
		FrictionMode = FrictionMode.StationMannings;
	}

	public bool Matches(CrossSection other)
	{
		if (Polyline != null || other.Polyline != null)
		{
			if (Polyline == null)
			{
				if (other.Polyline.Count > 0)
				{
					return false;
				}
			}
			else if (other.Polyline == null)
			{
				if (Polyline.Count > 0)
				{
					return false;
				}
			}
			else if (!Polyline.EqualsXY(other.Polyline))
			{
				return false;
			}
		}
		if (RiverName != other.RiverName)
		{
			return false;
		}
		if (ReachName != other.ReachName)
		{
			return false;
		}
		if (RiverStation != other.RiverStation)
		{
			return false;
		}
		if (NodeName != other.NodeName)
		{
			return false;
		}
		if (Description != other.Description)
		{
			return false;
		}
		if (FrictionMode != other.FrictionMode)
		{
			return false;
		}
		if (!ReachLengths.EqualsPiecewise(other.ReachLengths))
		{
			return false;
		}
		if (MinorLoss != other.MinorLoss)
		{
			return false;
		}
		if (Skew != other.Skew)
		{
			return false;
		}
		if (!LeveeProperties.EqualsPiecewise(other.LeveeProperties))
		{
			return false;
		}
		if (!ContractionExpansionCoefficients.EqualsPiecewise(other.ContractionExpansionCoefficients))
		{
			return false;
		}
		if (!HydraulicProperties.Matches(other.HydraulicProperties))
		{
			return false;
		}
		if (!IceProperties.EqualsPiecewise(other.IceProperties))
		{
			return false;
		}
		return true;
	}

	public static double TryParseRiverStationNumeric(string rs)
	{
		if (rs.EndsWith("*"))
		{
			rs = rs.Replace("*", "");
		}
		if (double.TryParse(rs, out var result))
		{
			return result;
		}
		return double.NaN;
	}

	internal void UnsetRiver()
	{
		RiverName = "";
		ReachName = "";
		RiverStation = "";
	}

	internal void ClearReachLengths()
	{
		ReachLengths.Left = double.NaN;
		ReachLengths.Right = double.NaN;
		ReachLengths.Channel = double.NaN;
	}

	internal void ClearBankProperties()
	{
		BankProperties.CenterStation = double.NaN;
		BankProperties.LeftStation = double.NaN;
		BankProperties.RightStation = double.NaN;
	}

	internal void ClearLeveeProperties()
	{
		LeveeProperties.LeftStation = double.NaN;
		LeveeProperties.RightStation = double.NaN;
		LeveeProperties.LeftElevation = double.NaN;
		LeveeProperties.RightElevation = double.NaN;
	}

	internal void SetRiver(River riv, LineIntersection intersection)
	{
		RiverName = riv.RiverName;
		ReachName = riv.ReachName;
		BankProperties.CenterStation = Polyline.DistanceAlong(intersection.BIntersection);
	}

	public double MValueToStation(double m)
	{
		double length = Polyline.Length;
		if (m <= -2.0)
		{
			return 0.0;
		}
		if (m < -1.0)
		{
			return BankProperties.LeftStation * (2.0 + m);
		}
		if (m < 0.0)
		{
			return BankProperties.LeftStation + (BankProperties.CenterStation - BankProperties.LeftStation) * (m + 1.0);
		}
		if (m < 1.0)
		{
			return BankProperties.CenterStation + (BankProperties.RightStation - BankProperties.CenterStation) * m;
		}
		if (m < 2.0)
		{
			return BankProperties.RightStation + (length - BankProperties.RightStation) * (m - 1.0);
		}
		return length;
	}

	public double StationToMValue(double station)
	{
		double length = Polyline.Length;
		if (station <= 0.0)
		{
			return -2.0;
		}
		if (station < BankProperties.LeftStation)
		{
			return -2.0 + station / BankProperties.LeftStation;
		}
		if (station < BankProperties.RightStation)
		{
			return -1.0 + 2.0 * (station - BankProperties.LeftStation) / (BankProperties.RightStation - BankProperties.LeftStation);
		}
		if (station < length)
		{
			return 1.0 + (station - BankProperties.RightStation) / (length - BankProperties.RightStation);
		}
		return 2.0;
	}

	public override string ToString()
	{
		return RiverName + "-" + ReachName + "-" + RiverStation;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
