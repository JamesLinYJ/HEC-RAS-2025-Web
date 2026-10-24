using System;

namespace Geospatial.Vectors.Rasterizers;

public class MeshClip
{
	public static readonly MeshClip Empty = new MeshClip();

	public Memory<float> CellWaterSurfaces { get; set; }

	public Memory<float> PixelDepths { get; set; }
}
