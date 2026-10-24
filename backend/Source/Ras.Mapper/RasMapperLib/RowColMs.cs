using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace RasMapperLib;

[ComVisible(false)]
public class RowColMs : List<RowColM>
{
	public void Add(int r, int c)
	{
		RowColM rowColM = default;
		rowColM.Assign(r, c);
		Add(rowColM);
	}

	public void AddNew(RowColM MRowCol)
	{
		if (Count == 0)
		{
			Add(MRowCol);
		}
		else if (!this.Last().Equal(MRowCol))
		{
			Add(MRowCol);
		}
	}

	public void AddRange(RowColMs RowColMs)
	{
		checked
		{
			if (Capacity < Count + RowColMs.Count)
			{
				Capacity = Count + RowColMs.Count;
			}
			foreach (RowColM RowColM in RowColMs)
			{
				Add(RowColM);
			}
		}
	}

	public void AddRange(params RowColMs[] RowColMsParamArray)
	{
		foreach (RowColMs rowColMs in RowColMsParamArray)
		{
			AddRange(rowColMs);
		}
	}
}
