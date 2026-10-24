using System.Collections.Generic;

namespace RasMapperLib;

public class CulvertShapeType
{
	public string Name;

	public int ID;

	public List<CulvertChartNumber> AllowedChartNumbers;

	public CulvertShapeType(string name, int id, params CulvertChartNumber[] chartNums)
	{
		Name = name;
		ID = id;
		AllowedChartNumbers = new List<CulvertChartNumber>();
		foreach (CulvertChartNumber chartNum in chartNums)
		{
			AllowedChartNumbers.Add(chartNum);
		}
	}
}
