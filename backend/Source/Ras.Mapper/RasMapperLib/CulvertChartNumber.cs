using System.Collections.Generic;

namespace RasMapperLib;

public class CulvertChartNumber
{
	public string Name;

	public int ID;

	public List<CulvertScaleNumber> AvailableScaleNumbers;

	public CulvertChartNumber(string name, int id, params CulvertScaleNumber[] scaleNums)
	{
		Name = name;
		ID = id;
		AvailableScaleNumbers = new List<CulvertScaleNumber>();
		foreach (CulvertScaleNumber scaleNum in scaleNums)
		{
			AvailableScaleNumbers.Add(scaleNum);
		}
	}
}
