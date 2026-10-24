using System;
using System.Collections.Generic;
using System.Reflection;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers.Attributes;
using Utility.Attributes;

namespace Geospatial.Rendering.Layers;

public static class IRenderLayerExtensions
{
	public static bool Render(this IRenderLayer irl, PlotRequest pr)
	{
		object cache = null;
		return irl.Render(pr, ref cache);
	}

	public static PlotRequest Render(this IRenderLayer irl, RasterDefinition rdef)
	{
		object cache = null;
		PlotRequest plotRequest = new PlotRequest(rdef);
		irl.Render(plotRequest, ref cache);
		return plotRequest;
	}

	public static Dictionary<PlotOption, PropertyInfo> GetPlotOptions(this IRenderLayer irl)
	{
		Dictionary<PlotOption, PropertyInfo> plotOptions = new Dictionary<PlotOption, PropertyInfo>();
		foreach (MemberInfo membersWithAttribute in irl.GetType().GetMembersWithAttribute(typeof(PlotOption), true, typeof(bool), typeof(Enum)))
		{
			if (membersWithAttribute is PropertyInfo plotOptions2)
			{
				plotOptions[membersWithAttribute.GetCustomAttribute<PlotOption>()] = plotOptions2;
				continue;
			}
			throw new InvalidCastException("PlotOption can only be used on properties, not fields.");
		}
		return plotOptions;
	}
}
