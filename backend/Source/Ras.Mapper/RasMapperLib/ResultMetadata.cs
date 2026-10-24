using System;

namespace RasMapperLib;

public class ResultMetadata
{
	public string ErrorMessage;

	public DateTime[] ProfileDateTimes;

	public string[] ProfileNames;

	public bool SteadyHydraulics;

	public bool QuasiUnsteadyHydraulics;

	public bool UnsteadyHydraulics;

	public bool UnsteadySediment;

	public bool UncertaintyAnalysis;
}
