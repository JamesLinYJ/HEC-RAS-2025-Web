using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class WMSLayer : ImageLayer
{
	public override bool CanZoomToLayer { get; }

	public override Extent Extent => Extent.Infinite;

	public WMSLayer()
		: base("Web Map Service")
	{
		CanZoomToLayer = false;
	}

	public WMSLayer(string name, string filename)
		: base(name, filename)
	{
		CanZoomToLayer = false;
	}

	private bool CanRender()
	{
		if (SharedData.SRSProjection == null)
		{
			if (Operators.CompareString(SharedData.SRSFilename, "", TextCompare: false) == 0)
			{
				ErrorMessage = "Error loading WMS imagery - SRS Projection not specifed.";
			}
			else
			{
				ErrorMessage = "Error loading WMS imagery - SRS Projection file: '" + SharedData.SRSFilename + "' not found.";
			}
			return false;
		}
		ErrorMessage = "";
		return true;
	}
}
