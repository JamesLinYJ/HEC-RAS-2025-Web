namespace RasMapperLib.Mesh;

public struct FaceAroundFacePoint
{
	public int faceIndex;

	public int faceOrientation;

	public double bearing;

	public bool isPerimeter;

	public FaceAroundFacePoint(int faceIndex, int faceOrientation, double bearing, bool isPerimeter)
	{
		this = default;
		this.faceIndex = faceIndex;
		this.faceOrientation = faceOrientation;
		this.bearing = bearing;
		this.isPerimeter = isPerimeter;
	}

	public static int CompareBearings(FaceAroundFacePoint a, FaceAroundFacePoint b)
	{
		if (a.bearing < b.bearing)
		{
			return -1;
		}
		if (a.bearing == b.bearing)
		{
			return 0;
		}
		return 1;
	}
}
