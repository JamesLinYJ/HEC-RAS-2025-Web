using System.Diagnostics;

namespace RasMapperLib.Mapping;

[DebuggerDisplay("Map Point {Index}: Z={Z}, M={M}")]
public class XSISMapPoint
{
	public int Index;

	public float Z;

	public float M;

	public float StreamNX;

	public float StreamNY;

	public XSISMapPoint(int index, float z, float m, float streamNX, float streamNY)
	{
		Index = index;
		Z = z;
		M = m;
		StreamNX = streamNX;
		StreamNY = streamNY;
	}
}
