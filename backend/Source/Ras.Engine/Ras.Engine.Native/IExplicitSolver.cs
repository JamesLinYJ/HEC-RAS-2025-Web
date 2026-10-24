using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Utility.Progress;

namespace Ras.Engine.Native;

public interface IExplicitSolver : IDisposable
{
	Action<IExplicitSolver> TimestepDone { get; set; }

	long TMicro { get; }

	ProgressReporter Reporter { get; set; }

	void CopyCellWaterSurface(double[] doubleValues);

	void CopyCellVolume(double[] doubleValues);

	void CopyFaceVelocity(float[] floatValues);

	void Init(Mesh mesh, MeshConnectivity connectivity, MeshPropertyTables tables, MeshWaterState ws, HydraulicMeshComputer hmc, LocalCoordinates localCoordinates, Solver.FaceFlag[] faceFlags, List<BoundaryCondition> flowBoundaryConditions, List<BoundaryCondition> stageBoundaryConditions, List<BoundaryCondition> normalDepthBoundaryConditions, SpatiotemporalBoundaryCondition precipBoundaryCondition, SolverControl control, ComputeOptions opts);

	ExplicitSolverResult Solve(long duration, long dt_micro);
}
