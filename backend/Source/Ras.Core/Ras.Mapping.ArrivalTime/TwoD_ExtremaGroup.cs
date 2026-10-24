namespace Ras.Mapping.ArrivalTime;

public class TwoD_ExtremaGroup
{
	public int TwoD_Area;

	public int StartCell;

	public int NumCells;

	public int EndCell;

	public int[,] Info;

	public int[] Extrema;

	public float[,] WSValues;

	public TwoD_ExtremaGroup(int[,] info, int[] extrema, float[,] wsValues, int twoD_area, int startCell)
	{
		Info = info;
		Extrema = extrema;
		WSValues = wsValues;
		TwoD_Area = twoD_area;
		StartCell = startCell;
		NumCells = info.GetLength(0);
		EndCell = startCell + NumCells - 1;
	}

	public bool Contains(int twoD_area, int cellIndex)
	{
		if (twoD_area == TwoD_Area && cellIndex >= StartCell && cellIndex <= EndCell)
		{
			return true;
		}
		return false;
	}

	public ExtremaType ExtremaStartType(int cellIndex)
	{
		return (ExtremaType)Info[cellIndex, 2];
	}
}
