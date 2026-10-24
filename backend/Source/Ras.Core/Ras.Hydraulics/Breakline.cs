using System.Diagnostics;
using Geospatial;
using Geospatial.Vectors;
using Ras.Hydraulics.Breaklines;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics;

[DebuggerDisplay("{ToString()}")]
public class Breakline : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
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
	[Metadata("Name")]
	public string Name { get; set; }

	[HashContributor]
	[ComplexMetadata]
	public PointGenerationProperties PointGenerationProperties { get; set; } = new PointGenerationProperties();

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
