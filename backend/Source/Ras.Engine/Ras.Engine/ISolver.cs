using System;
using Geospatial.Vectors;
using Ras.Engine.Structures;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Engine;

public interface ISolver
{
	public enum Status
	{
		OK,
		VOLUME_NEGATIVE,
		ELEVATION_CHANGE,
		NAN_STATE,
		ERROR
	}

	long TMicros { get; }

	ProgressReporter Reporter { get; set; }

	SolverControl Control { get; }

	ReadOnlySpan<double> GetCellWaterSurface();

	ReadOnlySpan<double> GetCellVolume();

	ReadOnlySpan<double> GetFaceVelocity();

	ReadOnlySpan<double> GetFaceArea();

	Span<ComputeControl.CellFlag> GetCellFlags();

	void SetPrincipalState(double[] cellWaterSurface, double[] cellVolume, double[] faceVelocity);

	MeshWaterState MaybeGetMeshWaterState();

	Mesh GetMesh();

	BoundaryConditionCollection GetBoundaryConditions();

	void ApplyBoundaryConditions(double absTime, double dt);

	HydraulicStructureCollection GetHydraulicStructures();

	void ApplyHydraulicStructures(double absTime, double dt);

	double MaximumTimestep(out Solver.StepLimitation stepLimitation, out int limitingCell, out int limitingFace);

	void Prepare(ProgressReporter pr = null);

	void CheckpointState();

	void RevertState();

	void CommitState();

	void Cleanup();

	void ReportState(OperationResult opResult, bool userTimestep);

	Status SolveStep(long TMicros, long DtMicros, long DtMicrosPrevious, out int failureCell);
}
