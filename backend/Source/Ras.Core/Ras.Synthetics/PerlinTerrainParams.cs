using Geospatial.Rasters;
using Geospatial.Rasters.Synthetics;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class PerlinTerrainParams : BaseParams
{
	private IResample<float> _trRes;

	public override double SolveDt => 0.01;

	public override double SolveDuration => 10.0;

	public override int ReportFrequency => 1;

	public override float NValue => 0.03f;

	public PerlinTerrainParams()
	{
		Extent ext = new Extent(0.0, 64.0, 0.0, 64.0);
		_trRes = new PerlinNoise(ext, 2, 0.5, 0.125).WithTransformer(new ScaleValueTransformer<float>(5f)).InterpolateWith(new PerlinNoise(ext, 4, 0.5), 0.3f);
	}

	public override IResample<float> TerrainResampler()
	{
		return _trRes;
	}
}
