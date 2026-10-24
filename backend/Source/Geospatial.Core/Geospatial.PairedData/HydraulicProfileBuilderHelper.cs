using System.Collections.Generic;

namespace Geospatial.PairedData;

public class HydraulicProfileBuilderHelper
{
	public List<HydraulicProperties> PreserveKBuffer;

	public List<HydraulicProperties> ModHPBuffer;

	public List<double> SampleElevations;

	public List<double> GoodSampleElevations;

	public HydraulicProfileBuilderHelper()
	{
		PreserveKBuffer = new List<HydraulicProperties>();
		ModHPBuffer = new List<HydraulicProperties>();
		SampleElevations = new List<double>();
		GoodSampleElevations = new List<double>();
	}
}
