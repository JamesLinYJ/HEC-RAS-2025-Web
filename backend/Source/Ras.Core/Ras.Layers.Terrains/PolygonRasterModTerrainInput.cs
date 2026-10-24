using System;
using Geospatial;
using Geospatial.Rasters;
using Utility;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.Terrains;

public class PolygonRasterModTerrainInput : TerrainRasterModificationLayer<PolygonRasterMod>, ITerrainInput, IDataHash, IDisposable, ISpatialVersion
{
	private Versioned<PolygonRasterModificationResampler> _resamplerVersioned = new Versioned<PolygonRasterModificationResampler>();

	[Metadata("SortHighToLow")]
	public bool SortedHighestToLowest => false;

	public PolygonRasterModificationResampler Resampler => _resamplerVersioned.Get(VersionXY, BuildResampler);

	public override string TypeName => "Polygon";

	public PolygonRasterModTerrainInput(Terrain tr, string name)
		: base(tr)
	{
		Name = name;
	}

	internal PolygonRasterModTerrainInput()
	{
	}

	private PolygonRasterModificationResampler BuildResampler()
	{
		return new PolygonRasterModificationResampler(Features);
	}

	public override IResample<float> GetResampler()
	{
		return Resampler;
	}
}
