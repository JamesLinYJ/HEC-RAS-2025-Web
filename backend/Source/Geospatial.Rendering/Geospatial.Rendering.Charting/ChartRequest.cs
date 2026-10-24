using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Rendering.Charting;

public class ChartRequest : IChartRequest
{
	public string Title { get; }

	public IList<IList<ISeriesRequest>> Series { get; }

	public ChartRequest(string title)
	{
		Title = title;
		Series = new List<IList<ISeriesRequest>>();
	}

	public void Add(ISeriesRequest series)
	{
		Series.Add(new ISeriesRequest[1] { series });
	}

	public void AddRange(IList<ISeriesRequest> series)
	{
		foreach (ISeriesRequest sery in series)
		{
			Add(sery);
		}
	}

	public void AddGroup(IList<ISeriesRequest> series)
	{
		Series.Add(series);
	}

	public void AddGroup(params ISeriesRequest[] series)
	{
		Series.Add(series);
	}

	public void TryAddGroup(params ISeriesRequest[] series)
	{
		if (series.Any((ISeriesRequest a) => a == null))
		{
			Series.Add(series.Where((ISeriesRequest s) => s != null).ToArray());
		}
		else
		{
			Series.Add(series);
		}
	}
}
