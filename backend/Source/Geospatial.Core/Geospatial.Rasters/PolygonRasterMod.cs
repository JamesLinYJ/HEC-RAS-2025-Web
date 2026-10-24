using System.Diagnostics;
using System.Threading;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Geospatial.Rasters;

[DebuggerDisplay("Modification Type: {ModificationType}, Value: {Value}")]
public class PolygonRasterMod : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent, IRasterMod
{
	private Polygon _polygon;

	private VersionTracker _tracker;

	private long _version = 1L;

	private Freezer _freezer;

	private RasterModType _rasterModType;

	private float _value;

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _tracker.GetVersion(_polygon) + _version;
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

	[HashContributor]
	[Metadata("ModificationType")]
	public RasterModType ModificationType
	{
		get
		{
			return _rasterModType;
		}
		set
		{
			if (_rasterModType != value)
			{
				_rasterModType = value;
				Interlocked.Increment(ref _version);
			}
		}
	}

	[HashContributor]
	[Metadata("Value")]
	public float Value
	{
		get
		{
			return _value;
		}
		set
		{
			if (Value != value)
			{
				_value = value;
				Interlocked.Increment(ref _version);
			}
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

	public PolygonRasterMod()
	{
	}

	public PolygonRasterMod(Polygon pg, RasterModType modType, float v)
	{
		Polygon = pg;
		ModificationType = modType;
		Value = v;
	}

	private PolygonRasterMod(PolygonRasterMod mod)
	{
		Polygon = mod.Polygon;
		ModificationType = mod.ModificationType;
		Value = mod.Value;
	}

	public PolygonRasterMod Clone()
	{
		return new PolygonRasterMod(this);
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
