using Utility.ContextActions;

namespace Geospatial.Rendering.Charting;

public class ContextChartRequest : ContextBase
{
	public IChartRequest ChartRequest { get; set; }

	public ContextChartRequest(string name, bool enabled, IChartRequest chartRequest)
		: base(name, enabled)
	{
		ChartRequest = chartRequest;
	}
}
