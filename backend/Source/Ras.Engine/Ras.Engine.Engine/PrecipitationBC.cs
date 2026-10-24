using System;
using Geospatial;
using Geospatial.Vectors;

namespace Ras.Engine.Engine;

public class PrecipitationBC : SpatiotemporalBoundaryCondition
{
	public double[] CellPrecipRate;

	private readonly Mesh _mesh;

	public PrecipitationBC(double[] sourceTimes, Func<int, float[]> getSourceData, float scaleFactor, JaggedWeights<float> cellWeights, Mesh mesh)
		: base(sourceTimes, getSourceData, scaleFactor, cellWeights)
	{
		_mesh = mesh;
		CellPrecipRate = null;
	}

	public void Prepare()
	{
		CellPrecipRate = new double[_mesh.CellCount];
	}

	public void Update(double time, double timestep)
	{
		UpdateScalar(CellPrecipRate, time, timestep);
	}
}
