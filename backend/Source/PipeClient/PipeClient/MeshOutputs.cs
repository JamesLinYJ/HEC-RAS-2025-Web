namespace PipeClient;

public class MeshOutputs
{
	public int[] CellFacesCount;

	public int[] CellFacesStartIndex;

	public int[] CellFaces;

	public double[] FacePointX;

	public double[] FacePointY;

	public bool[] FacePointIsPerim;

	public int[] FaceFPA;

	public int[] FaceFPB;

	public int[] FaceCellA;

	public int[] FaceCellB;

	public bool[] FaceIsPerimeter;

	public int[] FaceInternalStartIndex;

	public int[] FaceInternalCount;

	public double[] FaceInternalX;

	public double[] FaceInternalY;

	public int CellMaxIndex;

	public float CellMaxSize;

	public float CellMinSize;

	public float CellAvgSize;

	public int Status;

	public int BadPtCount;

	public int[] BadPoints;

	public string StatusMsg;
}
