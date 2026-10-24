using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics.Analysis;

public static class DecayAnalysis
{
	public static List<(double Time, double Omega)> TrackDecay(Solver solver)
	{
		return BaseAnalysis.TrackScalar(solver, (MeshTimestep mt) => ExtractOmega(solver.Mesh, mt.WaterState), 1E-10);
	}

	public static List<(double Time, double Omega)> TrackDecay(ComputeDriver driver)
	{
		return BaseAnalysis.TrackScalar(driver, (MeshTimestep mt) => ExtractOmega(driver.GlobalMesh, mt.WaterState), 1E-10);
	}

	public static double ExtractOmega(Mesh mesh, MeshWaterState ws)
	{
		double computed = 0.0;
		double computed2 = 0.0;
		for (int i = 0; i < mesh.CellCount; i++)
		{
			Vector vector = new Vector(mesh.CellCenters[i].X, mesh.CellCenters[i].Y);
			double length = vector.Length;
			double cellSimpleArea = mesh.CellSimpleArea(i);
			computed += cellSimpleArea * Math.Pow(length, 2.0);
			Vector other = mesh.MetadataCache.CellComputes.VelocityWLS(i, ws.FaceArea, ws.FaceVelocity);
			computed2 += vector.CrossProduct(other) * cellSimpleArea;
		}
		return computed2 / computed;
	}

	public static double DecayTimeHalfSaturation(List<(double Time, double Omega)> timeSeries)
	{
		int index = timeSeries.Count / 10;
		int index2 = timeSeries.Count - 1;
		double time = timeSeries[index].Time;
		double omega = timeSeries[index].Omega;
		double time2 = timeSeries[index2].Time;
		double omega2 = timeSeries[index2].Omega;
		return (omega2 * time2 - omega * time) / (omega - omega2);
	}
}
