using System.Diagnostics;
using Geospatial;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Hydraulics.Breaklines;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.Classic;

[DebuggerDisplay("{ID}")]
public class Structure : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
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
	[DatasetName("Station Elevation")]
	public StationElevationProfile StationElevation { get; set; }

	[HashContributor]
	[ComplexMetadata]
	public StructureIdentifier ID { get; set; } = new StructureIdentifier();

	[HashContributor]
	[Metadata("Weir Width")]
	public float WeirWidth { get; set; }

	[HashContributor]
	[Metadata("Weir US Slope")]
	public float UpstreamSlope { get; set; }

	[HashContributor]
	[Metadata("Weir DS Slope")]
	public float DownstreamSlope { get; set; }

	[HashContributor]
	[ComplexMetadata(ColumnNamePrefix = "US ")]
	public StructureConnection UpstreamConnection { get; set; } = new StructureConnection();

	[HashContributor]
	[ComplexMetadata(ColumnNamePrefix = "DS ")]
	public StructureConnection DownstreamConnection { get; set; } = new StructureConnection();

	[HashContributor]
	[Metadata("SNN ID")]
	public int SNNID { get; set; }

	[HashContributor]
	[Metadata("Culverts")]
	public int CulvertCount { get; }

	[HashContributor]
	[Metadata("Gates")]
	public int GateCount { get; }

	[HashContributor]
	[Metadata("LW Velocity Into 2D")]
	public bool LWVelocityInto2D { get; set; }

	[HashContributor]
	[ComplexMetadata]
	public PointGenerationProperties PointGenerationProperties { get; set; } = new PointGenerationProperties();

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
