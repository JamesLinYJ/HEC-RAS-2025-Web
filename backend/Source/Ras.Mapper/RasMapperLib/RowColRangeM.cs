using System.Drawing;

namespace RasMapperLib;

public struct RowColRangeM
{
	public int MinRow;

	public int MaxRow;

	public int MinCol;

	public int MaxCol;

	public bool InRange(int r, int c)
	{
		if (r < MinRow || r > MaxRow || c < MinCol || c > MaxCol)
		{
			return false;
		}
		return true;
	}

	public int Width()
	{
		return checked(MaxCol - MinCol + 1);
	}

	public int Height()
	{
		return checked(MaxRow - MinRow + 1);
	}

	public System.Drawing.Rectangle ComputeRectangle()
	{
		return checked(new System.Drawing.Rectangle(MinCol, MinRow, MaxCol - MinCol + 1, MaxRow - MinRow + 1));
	}
}
