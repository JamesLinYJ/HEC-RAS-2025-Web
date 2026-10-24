using System;
using System.Collections.Generic;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.ParticleTracking;

public class ParticleRenderLayer : RenderLayer
{
	private readonly List<Particle> _particles = new List<Particle>();

	public override Extent ValidExtent
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		throw new NotImplementedException();
	}
}
