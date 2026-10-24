using System.Collections.Generic;
using System.Linq;

namespace RasMapperLib;

public class RibbonTINFactory : TINFactory
{
	public static DevelopmentTin CreateTIN(List<IPoints> ribbons, double nearPointTolerance)
	{
		return new RibbonTINFactory(nearPointTolerance).CreateTIN(ribbons);
	}

	private RibbonTINFactory(double NearPointTolerance)
		: base(NearPointTolerance)
	{
	}

	private DevelopmentTin CreateTIN(List<IPoints> ribbons)
	{
		if (ribbons == null || ribbons.Count < 2)
		{
			return null;
		}
		List<int> intList = new List<int>();
		List<int> intListList = new List<int>();
		checked
		{
			foreach (PointMs ribbon in ribbons)
			{
				intList.Add(_points.Count);
				int ordinal = 0;
				int count = ribbon.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					if (i == 0)
					{
						_points.Add(ribbon[i]);
						ordinal++;
					}
					else if (!_points.Last().CloseTo(ribbon[i], _tolerance))
					{
						_points.Add(ribbon[i]);
						ordinal++;
					}
				}
				intListList.Add(ordinal);
			}
			int count2 = ribbons.Count - 2;
			for (int j = 0; j <= count2; j++)
			{
				int ordinal2 = intListList[j];
				int ordinal3 = intListList[j + 1];
				int ordinal4 = intList[j];
				int ordinal5 = ordinal4;
				int ordinal6 = intList[j + 1];
				int ordinal7 = ordinal6;
				while (ordinal5 - ordinal4 != ordinal2 - 1 || ordinal7 - ordinal6 != ordinal3 - 1)
				{
					int ordinal8 = ordinal5 + 1;
					int ordinal9 = ordinal7 + 1;
					if (ordinal8 - ordinal4 >= ordinal2)
					{
						AddTriangle(ordinal5, ordinal7, ordinal9, -1, -1, -1);
						ordinal7++;
						continue;
					}
					if (ordinal9 - ordinal6 >= ordinal3)
					{
						AddTriangle(ordinal5, ordinal7, ordinal8, -1, -1, -1);
						ordinal5++;
						continue;
					}
					double distanceTo = _points[ordinal5].DistanceTo(_points[ordinal9]);
					double distanceTo2 = _points[ordinal7].DistanceTo(_points[ordinal8]);
					if (distanceTo > distanceTo2)
					{
						AddTriangle(ordinal5, ordinal7, ordinal8, -1, -1, -1);
						ordinal5++;
					}
					else
					{
						AddTriangle(ordinal5, ordinal7, ordinal9, -1, -1, -1);
						ordinal7++;
					}
				}
			}
			return new DevelopmentTin(_triangles, _points);
		}
	}
}
