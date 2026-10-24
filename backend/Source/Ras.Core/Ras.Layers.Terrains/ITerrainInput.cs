using System;
using Geospatial;
using Geospatial.Rasters;
using H5Assist;
using Utility.Hashing;

namespace Ras.Layers.Terrains;

public interface ITerrainInput : IDataHash, IDisposable, ISpatialVersion
{
	string TypeName { get; }

	string Name { get; set; }

	Terrain Terrain { get; }

	IResample<float> GetResampler();

	bool IsValid(out string err);

	bool TryInit(Terrain terrain, H5Reader hr, string h5Path);

	void Save(H5Writer hw, string h5Path);

	void Switch(Action<RasterTerrainInput> ifRaster, Action<ConstantTerrainInput> ifConstant, Action<ChannelModTerrainInput> ifChannel)
	{
		if (this is RasterTerrainInput rasterTerrainInput)
		{
			ifRaster(rasterTerrainInput);
		}
		else if (this is ConstantTerrainInput constantTerrainInput)
		{
			ifConstant(constantTerrainInput);
		}
		else if (this is ChannelModTerrainInput channelModTerrainInput)
		{
			ifChannel(channelModTerrainInput);
		}
		else if (!(this is DebugTerrainInput))
		{
			throw new NotSupportedException();
		}
	}
}
