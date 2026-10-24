using System;
using System.Collections.Generic;

namespace Ras.Optimization;

public sealed class ManningZoneScaleParameter : ICalibrationParameter
{
	private readonly ManningZone _zone;

	public string Name { get; }

	public double Min { get; }

	public double Max { get; }

	public double Initial { get; }

	public ParameterScale Scale { get; }

	public bool AffectsManning => true;

	public IReadOnlyList<string> ProducesKeys => new string[1] { "manning-zone:" + _zone.Name };

	public IReadOnlyList<string> RequiresKeys => Array.Empty<string>();

	public ManningZoneScaleParameter(ManningZone zone, double minFactor, double maxFactor, double initialFactor = 1.0, string name = null, ParameterScale scale = ParameterScale.Linear)
	{
		if (zone == null)
		{
			throw new ArgumentNullException("zone");
		}
		if (maxFactor < minFactor)
		{
			throw new ArgumentException("Max factor must be >= Min factor.");
		}
		if (initialFactor < minFactor || initialFactor > maxFactor)
		{
			throw new ArgumentException("Initial factor must be within [Min, Max].");
		}
		if (scale == ParameterScale.Log && minFactor <= 0.0)
		{
			throw new ArgumentException("Log scale requires Min > 0.");
		}
		_zone = zone;
		Min = minFactor;
		Max = maxFactor;
		Initial = initialFactor;
		Scale = scale;
		Name = (string.IsNullOrEmpty(name) ? ("n-scale[zone:" + zone.Name + "]") : name);
	}

	public void Apply(double factor, CalibrationContext ctx)
	{
		ctx.SetZoneScale(_zone, factor);
		ctx.ManningDirty = true;
	}
}
