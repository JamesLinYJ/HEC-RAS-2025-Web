namespace Geospatial.Hydraulics;

internal readonly struct LDMStageDiagnostics
{
	internal bool Converged { get; }

	internal int Iterations { get; }

	internal float FinalVelocityError { get; }

	internal float TotalConveyance { get; }

	internal LDMStageDiagnostics(bool converged, int iterations, float finalVelocityError, float totalConveyance)
	{
		Converged = converged;
		Iterations = iterations;
		FinalVelocityError = finalVelocityError;
		TotalConveyance = totalConveyance;
	}
}
