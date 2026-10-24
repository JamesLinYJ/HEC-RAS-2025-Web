using System;

namespace Geospatial.Vectors.Rasterizers;

public class VectorMeshOutputs
{
	public Memory<float> OutputVx { get; set; }

	public Memory<float> OutputVy { get; set; }

	public Memory<float> OutputMagnitude { get; set; }
}
