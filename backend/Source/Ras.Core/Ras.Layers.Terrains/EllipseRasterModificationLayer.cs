using System;
using Geospatial;
using Geospatial.Rasters;
using Utility.Hashing;

namespace Ras.Layers.Terrains;

public class EllipseRasterModificationLayer : TerrainRasterModificationLayer<EllipseRasterMod>, ITerrainInput, IDataHash, IDisposable, ISpatialVersion
{
	public const string TypeNameConst = "Ellipse";

	public override string TypeName => "Ellipse";

	public override IResample<float> GetResampler()
	{
		throw new NotImplementedException();
	}
}
