using System;
using Geospatial;
using Geospatial.Rasters;
using H5Assist;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.Terrains;

public abstract class TerrainRasterModificationLayer<T> : TerrainFeatureInput<T>, ITerrainInput, IDataHash, IDisposable, ISpatialVersion where T : IFeatureRow, new()
{
	private string _hdfGroup;

	[Metadata("Type")]
	public abstract string TypeName { get; }

	protected override string HDF5Group => _hdfGroup;

	public TerrainRasterModificationLayer(Terrain tr)
	{
		Terrain = tr;
	}

	internal TerrainRasterModificationLayer()
	{
	}

	public abstract IResample<float> GetResampler();

	public bool IsValid(out string err)
	{
		err = "";
		return true;
	}

	public virtual void Save(H5Writer hw, string h5Path)
	{
		_hdfGroup = h5Path;
		Save(hw);
	}

	public virtual bool TryInit(Terrain terrain, H5Reader hr, string h5Path)
	{
		Terrain = terrain;
		_hdfGroup = h5Path;
		LoadMetadata(hr);
		return true;
	}

	public void Dispose()
	{
	}
}
