using System;
using System.Collections.Generic;

namespace Ras.Optimization;

public sealed class ManningClassScaleParameter : ICalibrationParameter
{
	private readonly string _className;

	public string Name { get; }

	public double Min { get; }

	public double Max { get; }

	public double Initial { get; }

	public ParameterScale Scale { get; }

	public bool AffectsManning => true;

	public IReadOnlyList<string> ProducesKeys => new string[1] { "manning:" + _className };

	public IReadOnlyList<string> RequiresKeys => Array.Empty<string>();

	public ManningClassScaleParameter(string className, double minFactor, double maxFactor, double initialFactor = 1.0, string name = null, ParameterScale scale = ParameterScale.Linear)
	{
		if (string.IsNullOrEmpty(className))
		{
			throw new ArgumentException("Class name is required.", "className");
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
		_className = className;
		Min = minFactor;
		Max = maxFactor;
		Initial = initialFactor;
		Scale = scale;
		Name = (string.IsNullOrEmpty(name) ? ("n-scale[" + className + "]") : name);
	}

	public void Apply(double factor, CalibrationContext ctx)
	{
		float baselineManning = ctx.BaselineManning(_className);
		ctx.ManningTable.Set(_className, (float)((double)baselineManning * factor));
		ctx.ManningDirty = true;
	}
}
