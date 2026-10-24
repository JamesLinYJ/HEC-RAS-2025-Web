using System.Threading;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Geospatial.Rasters;

public class PolygonClassifierMod : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	private Polygon _polygon;

	private VersionTracker _tracker;

	private long _versionXy = 1L;

	private Freezer _freezer;

	private string _classification;

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _tracker.GetVersion(_polygon) + _versionXy;
		}
	}

	[HashContributor]
	[DatasetName("Polygon")]
	public Polygon Polygon
	{
		get
		{
			return _polygon;
		}
		set
		{
			_tracker.Assign(ref _polygon, value);
		}
	}

	[Metadata("Classification")]
	public string Classification
	{
		get
		{
			return _classification;
		}
		set
		{
			_classification = value;
			Interlocked.Increment(ref _versionXy);
		}
	}

	public Extent Extent => Polygon?.Extent ?? Extent.NullExtent;

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public PolygonClassifierMod()
	{
	}

	public PolygonClassifierMod(Polygon pg, string cls)
	{
		Polygon = pg;
		Classification = cls;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
