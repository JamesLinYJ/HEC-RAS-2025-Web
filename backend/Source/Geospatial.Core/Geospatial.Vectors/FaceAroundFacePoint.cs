namespace Geospatial.Vectors;

public struct FaceAroundFacePoint(int faceIndex, int faceOrientation, double bearing, bool isPerimeter)
{
	public int faceIndex = faceIndex;

	public int faceOrientation = faceOrientation;

	public double bearing = bearing;

	public bool isPerimeter = isPerimeter;

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
