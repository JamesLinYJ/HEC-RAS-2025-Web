namespace RasMapperLib.TinHelpers;

public struct InsertionHoleEdge
{
	public int p0;

	public int p1;

	public int outsideTriangle;

	public int insideTriangle;

	public InsertionHoleEdge(int p0, int p1, int atOutside, int atInside)
	{
		this = default;
		this.p0 = p0;
		this.p1 = p1;
		outsideTriangle = atOutside;
		insideTriangle = atInside;
	}
}
