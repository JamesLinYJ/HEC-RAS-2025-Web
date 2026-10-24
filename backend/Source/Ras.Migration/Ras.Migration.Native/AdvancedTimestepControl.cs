using System.Collections.Generic;

namespace Ras.Migration.Native;

public class AdvancedTimestepControl
{
	public bool UseCourant;

	public bool UseTimeSeries;

	public float MaxCourant;

	public float MinCourant;

	public int CountToDouble;

	public int MaxDoubleCount;

	public int MaxHalveCount;

	public bool UseResidenceTime;

	public List<string> ComputationTimestepTs = new List<string>();
}
