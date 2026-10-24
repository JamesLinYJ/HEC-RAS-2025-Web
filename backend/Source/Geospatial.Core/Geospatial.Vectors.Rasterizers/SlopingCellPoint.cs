using System;

namespace Geospatial.Vectors.Rasterizers;

public struct SlopingCellPoint(int pxIdx, Memory<float> fpWeights, Memory<float> faceWeights)
{
	public int Index = pxIdx;

	public Memory<float> FacePointWeights = fpWeights;

	public Memory<float> VelocityWeights = faceWeights;
}
