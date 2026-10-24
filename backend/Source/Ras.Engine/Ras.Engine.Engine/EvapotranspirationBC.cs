using System;
using Geospatial;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;

namespace Ras.Engine.Engine;

public class EvapotranspirationBC : SpatiotemporalBoundaryCondition
{
	public double[] CellEvapotransPotential;

	public double[] CellEvapotransRate;

	private readonly Mesh _mesh;

	public EvapotranspirationBC(double[] sourceTimes, Func<int, float[]> getSourceData, float scaleFactor, JaggedWeights<float> cellWeights, Mesh mesh)
		: base(sourceTimes, getSourceData, scaleFactor, cellWeights)
	{
		_mesh = mesh;
		CellEvapotransPotential = null;
		CellEvapotransRate = null;
	}

	public void Prepare()
	{
		CellEvapotransRate = new double[_mesh.CellCount];
		CellEvapotransPotential = new double[_mesh.CellCount];
	}

	public void Update(double time, double timestep, double[] cellPrecipRate, float[] cellDepth, double[] cellArea)
	{
		UpdateScalar(CellEvapotransPotential, time, timestep);
		HydraulicMeshComputer metadataCache = _mesh.MetadataCache;
		Func<int, double> func = ((cellPrecipRate != null) ? ((Func<int, double>)((int i) => cellPrecipRate[i])) : ((Func<int, double>)((int i) => 0.0)));
		for (int intervalIndex = 0; intervalIndex < _mesh.CellCount; intervalIndex++)
		{
			double af = cellArea[intervalIndex] / metadataCache.CellPolygonAreas[intervalIndex];
			SolveStep(timestep, func(intervalIndex), af, cellDepth[intervalIndex], CellEvapotransPotential[intervalIndex], out CellEvapotransRate[intervalIndex]);
		}
	}

	public static void SolveStep(double dt, double p, double af, double h, double ET0, out double Ev)
	{
		double eT0Term = Math.Min(ET0, p);
		double eT0AfTerm = Math.Min(ET0 - eT0Term, af * h / dt);
		Ev = eT0Term + eT0AfTerm;
	}
}
