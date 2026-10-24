using Geospatial;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers;

public class MeshArc : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
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

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
