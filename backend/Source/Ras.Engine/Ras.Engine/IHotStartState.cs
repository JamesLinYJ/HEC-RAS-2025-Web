namespace Ras.Engine;

public interface IHotStartState
{
	double[] CellElevation { get; }

	double[] FaceVelocity { get; }

	long dtMicros { get; }
}
