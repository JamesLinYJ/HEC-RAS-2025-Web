using System;

namespace Ras.Mapping;

public static class StaticMapAccumulators
{
	public static float Max(float incidental, float accumulated)
	{
		return Math.Max(incidental, accumulated);
	}

	public static Func<float, float, float> DurationOver(float timestep, float threshold)
	{
		return (float incident, float accumulated) =>
		{
			if (incident <= threshold)
			{
				return accumulated;
			}
			return (accumulated == -9999f) ? timestep : (timestep + accumulated);
		};
	}
}
