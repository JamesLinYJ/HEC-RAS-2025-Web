using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RasMapperLib.ArrivalTime;

[ComVisible(false)]
public class MultiLocationWindowSeries
{
	public List<WindowSeries> Locations;

	public int Count
	{
		get
		{
			if (Locations == null)
			{
				return 0;
			}
			return Locations.Count;
		}
	}

	public WindowSeries this[int idx] => Locations[idx];

	public MultiLocationWindowSeries(List<WindowSeries> locations)
	{
		Locations = locations;
	}

	public MultiLocationWindowSeries()
	{
		Locations = new List<WindowSeries>();
	}

	public void Add(WindowSeries location)
	{
		Locations.Add(location);
	}
}
