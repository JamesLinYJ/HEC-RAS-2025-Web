using System.Diagnostics;
using Geospatial;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.Classic;

[DebuggerDisplay("{Name}")]
public class GateOpening : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	private Polyline _polyline;

	private VersionTracker _tracker;

	public long VersionXY => _tracker.GetVersion(_polyline);

	[HashContributor]
	[UserSettable(true)]
	[DatasetName("Centerline")]
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
	[ComplexMetadata]
	public StructureIdentifier StructureID { get; set; } = new StructureIdentifier();

	[HashContributor]
	[Metadata("Struct ID")]
	public int StructueIndex { get; }

	[HashContributor]
	[Metadata("Gate Group Name")]
	public string GroupName { get; set; }

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	[HashContributor]
	[Metadata("Gate Group ID")]
	public int GateGroupID { get; }

	[HashContributor]
	[Metadata("Station")]
	public float Station { get; set; }

	[HashContributor]
	[Metadata("Points")]
	public int PointCount => Polyline?.Count ?? 0;

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
