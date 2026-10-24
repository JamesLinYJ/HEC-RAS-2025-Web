using System;
using Geospatial.Vectors;
using Utility.Progress;

namespace Ras.Engine;

public interface IFaceApportionment
{
	const double DefaultAbsTolEta = 0.01;

	const double DefaultRelKTol = 0.01;

	double[] GetFaceFractions(Mesh mesh, MeshWaterState ws, double targetConveyance, double[] result = null);

	double[] GetFaceFractionsByStage(Mesh mesh, MeshWaterState ws, double stage, double[] result = null);

	void Prepare(ProgressReporter pr, BoundaryCondition bc, Mesh mesh, MeshWaterState ws, Func<MeshWaterState, int, double> faceConveyanceFunc);
}
