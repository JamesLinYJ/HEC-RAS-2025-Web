using System;
using Geospatial.Vectors;
using Ras.Engine.Engine;
using Utility.Progress;

namespace Ras.Engine;

public interface ITransport
{
	public enum Status
	{
		OK,
		INVALID_RANGE,
		NAN_STATE,
		ERROR
	}

	int ConstituentCount { get; }

	TransportControl Control { get; }

	double CurrentTime { get; }

	LoopConvergence.ConvStatus OuterConvergenceStatus { get; }

	Mesh GetMesh();

	ReadOnlySpan<double> GetCellConcentration(int constituent);

	void SetCellConcentration(int constituent, ReadOnlySpan<double> values);

	void Prepare(ProgressReporter pr = null);

	Status SolveStep(double absTime, double dt, out int failureCell);

	double MaximumTimestep(out int limitingCell);

	void CheckpointState();

	void RevertState();

	void CommitState();

	void Cleanup();
}
