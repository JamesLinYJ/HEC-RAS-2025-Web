using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Geospatial.Rasters;

public class EllipseRasterMod : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	private Ellipse _ellipse;

	private VersionTracker _tracker;

	private Freezer _freezer;

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _tracker.GetVersion(_ellipse);
		}
	}

	[HashContributor]
	[DatasetName("Ellipse")]
	public Ellipse Ellipse
	{
		get
		{
			return _ellipse;
		}
		set
		{
			_tracker.Assign(ref _ellipse, value);
		}
	}

	[Metadata("Modification Type")]
	public RasterModType ModificationType { get; set; }

	[Metadata("Value")]
	public float Value { get; set; }

	public Extent Extent => Extent.NullExtent;

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public EllipseRasterMod()
	{
	}

	public EllipseRasterMod(Ellipse e, RasterModType modType, float v)
	{
		Ellipse = e;
		ModificationType = modType;
		Value = v;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
