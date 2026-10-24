using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public struct RowColValueM
{
	public int Row;

	public int Col;

	public float Value;

	public void Assign(int r, int c, float v)
	{
		Row = r;
		Col = c;
		Value = v;
	}
}
