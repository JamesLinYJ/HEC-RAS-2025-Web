using System.Diagnostics;
using Geospatial;
using Geospatial.Vectors;
using Ras.Hydraulics.Rivers;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics;

[DebuggerDisplay("{ToString()}")]
public class River : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
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
	[Metadata("River Name")]
	public string RiverName { get; set; }

	[HashContributor]
	[Metadata("Reach Name")]
	public string ReachName { get; set; }

	[HashContributor]
	[ComplexMetadata(ColumnNamePrefix = "US ")]
	public Connection UpstreamConnection { get; set; } = new Connection();

	[HashContributor]
	[ComplexMetadata(ColumnNamePrefix = "DS ")]
	public Connection DownstreamConnection { get; set; } = new Connection();

	public River()
	{
	}

	public River(string rivName, string reachName, Polyline line)
	{
		RiverName = rivName;
		ReachName = reachName;
		Polyline = line;
	}

	public override string ToString()
	{
		return RiverName + "-" + ReachName;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
