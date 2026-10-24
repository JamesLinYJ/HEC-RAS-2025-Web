using System;
using Geospatial.Rasters;

namespace Geospatial.Rendering.TiledRenderingSystem;

internal struct RenderTaskDescriptor(long state, PyramidLevel pyramidLevel, Pixel tIdx) : IEquatable<RenderTaskDescriptor>
{
	public readonly long GlobalRenderState = state;

	public readonly Pixel TileIndex = tIdx;

	public readonly PyramidLevel PyramidLevel = pyramidLevel;

	public override int GetHashCode()
	{
		return HashCode.Combine(GlobalRenderState, TileIndex, PyramidLevel);
	}

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (obj is RenderTaskDescriptor rt)
		{
			return Equals(rt);
		}
		return false;
	}

	public bool Equals(RenderTaskDescriptor rt)
	{
		if (rt.GlobalRenderState == GlobalRenderState && rt.TileIndex == TileIndex)
		{
			return rt.PyramidLevel.Equals(PyramidLevel);
		}
		return false;
	}
}
