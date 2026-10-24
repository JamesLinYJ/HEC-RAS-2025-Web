using Geospatial;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.Terrains;

public class ChannelProfile : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	private VersionTracker _lineTracker;

	private Polyline _line;

	private VersionTracker _profileTracker;

	private StationElevationProfile _profile;

	public long VersionXY => _lineTracker.GetVersion(_line) + _profileTracker.GetVersion(_profile);

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	[HashContributor]
	[UserSettable(true)]
	[DatasetName("Line")]
	public Polyline Line
	{
		get
		{
			return _line;
		}
		set
		{
			_lineTracker.Assign(ref _line, value);
		}
	}

	[HashContributor]
	[UserSettable(false)]
	[DatasetName("Profile")]
	public StationElevationProfile Profile
	{
		get
		{
			return _profile;
		}
		set
		{
			_profileTracker.Assign(ref _profile, value);
		}
	}

	public Extent Extent => Line?.Extent ?? Extent.NullExtent;

	public ChannelProfile()
	{
	}

	public ChannelProfile(Polyline line, StationElevationProfile profile)
	{
		Line = line;
		Profile = profile;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
