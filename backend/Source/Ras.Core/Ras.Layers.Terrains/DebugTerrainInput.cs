using System;
using Geospatial;
using Geospatial.Rasters;
using H5Assist;
using Utility.Hashing;

namespace Ras.Layers.Terrains;

public class DebugTerrainInput : ITerrainInput, IDataHash, IDisposable, ISpatialVersion
{
	private readonly IResample<float> _res;

	public const string TypeNameConst = "Debug Terrain Input";

	public string TypeName => "Debug Terrain Input";

	public string Name { get; set; }

	public Terrain Terrain { get; }

	public long VersionXY => 1L;

	public DebugTerrainInput(Terrain tr, IResample<float> res)
	{
		Terrain = tr;
		_res = res;
	}

	public DebugTerrainInput()
	{
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

	public void Save(H5Writer hw, string h5Path)
	{
	}

	public bool TryInit(Terrain terrain, H5Reader hr, string h5Path)
	{
		return true;
	}

	public int GetDataHash()
	{
		return 0;
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
