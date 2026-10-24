using System;
using System.Threading;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers;

public class RandomWaitRenderLayer : RenderLayer
{
	[ThreadStatic]
	private static Random _random;

	private static Random Random => _random ?? (_random = new Random());

	public static int MaxWaitMilliseconds { get; set; } = 1000;

	public override Extent ValidExtent => Extent.Infinite;

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		Thread.Sleep(Random.Next(0, MaxWaitMilliseconds));
		return false;
	}
}
