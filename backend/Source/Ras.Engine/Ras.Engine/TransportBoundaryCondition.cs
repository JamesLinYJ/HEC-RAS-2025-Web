namespace Ras.Engine;

public class TransportBoundaryCondition
{
	public TransportBCType Type;

	public int[] Faces;

	public double[] ConcentrationByConstituent;

	public double[][] FaceConcentrationByConstituent;

	public TransportBoundaryCondition(TransportBCType type, int[] faces, double[] concentrationByConstituent = null)
	{
		Type = type;
		Faces = faces;
		ConcentrationByConstituent = concentrationByConstituent;
	}

	public double ConcentrationAt(int constituent, int slot)
	{
		double[][] faceConcentrationByConstituent = FaceConcentrationByConstituent;
		double[] faceConcentrationByConstituentArray = ((faceConcentrationByConstituent != null) ? faceConcentrationByConstituent[constituent] : null);
		if (faceConcentrationByConstituentArray == null)
		{
			return ConcentrationByConstituent[constituent];
		}
		return faceConcentrationByConstituentArray[slot];
	}

	public void AllocateFaceConcentrations(int constituentCount)
	{
		FaceConcentrationByConstituent = new double[constituentCount][];
		for (int i = 0; i < constituentCount; i++)
		{
			FaceConcentrationByConstituent[i] = new double[Faces.Length];
		}
	}
}
