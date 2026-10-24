using System;
using Geospatial.Rasters;

namespace Geospatial.Rendering.Layers;

public interface IShader
{
	void Shade(IResample<float> resampler, Memory<float> values, PlotRequest plot);
}
