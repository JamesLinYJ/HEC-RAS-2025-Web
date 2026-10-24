using System;
using System.Threading;
using Geospatial;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.Terrains;

public class ConstantTerrainInput : ITerrainInput, IDataHash, IDisposable, ISpatialVersion
{
	private ConstantValueResampler<float> _res;

	private Extent _ext;

	private float _val;

	private long _versionXy = 1L;

	public const string TypeNameConst = "Constant";

	[Metadata("Type")]
	public string TypeName => "Constant";

	[Metadata("Name")]
	public string Name { get; set; }

	[Metadata("Valid Extent")]
	public Extent ValidExtent
	{
		get
		{
			return _ext;
		}
		set
		{
			if (_ext.Equals(value)) return;
			_ext = value;
			Interlocked.Increment(ref _versionXy);
			if (_res != null)
			{
				_res.ValidExtent = value;
			}
		}
	}

	[Metadata("Value")]
	public float Value
	{
		get
		{
			return _val;
		}
		set
		{
			if (_val.Equals(value)) return;
			_val = value;
			Interlocked.Increment(ref _versionXy);
			if (_res != null)
			{
				_res.Value = value;
			}
		}
	}

	public Terrain Terrain { get; private set; }

	public long VersionXY => Interlocked.Read(ref _versionXy);

	public ConstantTerrainInput(Terrain tr, float floatValue, Extent ext, string name)
	{
		Terrain = tr;
		Value = floatValue;
		ValidExtent = ext;
		Name = name;
		RebuildResampler();
	}

	private ConstantTerrainInput()
	{
	}

	private void RebuildResampler()
	{
		_res = new ConstantValueResampler<float>(Value, ValidExtent, -9999f);
	}

	public bool TryInit(Terrain terrain, H5Reader hr, string h5Path)
	{
		if (!hr.ExistsAsGroup(h5Path))
		{
			return false;
		}
		Terrain = terrain;
		GeospatialH5Metadata.Read(this, hr, h5Path);
		RebuildResampler();
		return true;
	}

	public void Save(H5Writer hw, string h5Path)
	{
		GeospatialH5Metadata.Write(this, hw, h5Path);
	}

	public IResample<float> GetResampler()
	{
		return _res;
	}

	public bool IsValid(out string err)
	{
		err = "";
		return true;
	}

	public int GetDataHash()
	{
		return StableHash.Combine(ValidExtent.GetHashCode(), Value.GetHashCode(), Name.GetHashCode());
	}

	public void Dispose()
	{
	}

	public void FreezeOne()
	{
	}

	public void ThawOne()
	{
	}
}
