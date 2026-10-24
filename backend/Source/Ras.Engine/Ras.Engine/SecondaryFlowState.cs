using Geospatial.Vectors;

namespace Ras.Engine;

public class SecondaryFlowState
{
	public readonly double[] CellIntensity;

	public readonly double[] CellEquilibriumIntensity;

	public readonly double[] CellCurvature;

	public readonly double[] CellRoughnessParameter;

	public readonly Vector[] CellSurfaceSecondaryVelocity;

	public readonly Vector[] CellDispersion;

	public readonly double[] FaceMomentumDispersion;

	public SecondaryFlowState(Mesh mesh)
		: this(mesh.CellCount, mesh.FaceCount)
	{
	}

	private SecondaryFlowState(int cellCount, int faceCount)
	{
		CellIntensity = new double[cellCount];
		CellEquilibriumIntensity = new double[cellCount];
		CellCurvature = new double[cellCount];
		CellRoughnessParameter = new double[cellCount];
		CellSurfaceSecondaryVelocity = new Vector[cellCount];
		CellDispersion = new Vector[cellCount];
		FaceMomentumDispersion = new double[faceCount];
	}

	public SecondaryFlowState Clone()
	{
		SecondaryFlowState secondaryFlowState = new SecondaryFlowState(CellIntensity.Length, FaceMomentumDispersion.Length);
		CellIntensity.CopyTo(secondaryFlowState.CellIntensity, 0);
		CellEquilibriumIntensity.CopyTo(secondaryFlowState.CellEquilibriumIntensity, 0);
		CellCurvature.CopyTo(secondaryFlowState.CellCurvature, 0);
		CellRoughnessParameter.CopyTo(secondaryFlowState.CellRoughnessParameter, 0);
		CellSurfaceSecondaryVelocity.CopyTo(secondaryFlowState.CellSurfaceSecondaryVelocity, 0);
		CellDispersion.CopyTo(secondaryFlowState.CellDispersion, 0);
		FaceMomentumDispersion.CopyTo(secondaryFlowState.FaceMomentumDispersion, 0);
		return secondaryFlowState;
	}
}
