using System;
using System.Collections.Generic;

namespace Ras.Optimization;

public sealed class TurbulenceParameter : ICalibrationParameter
{
	private readonly TurbulenceCoefficient _coefficient;

	public string Name { get; }

	public double Min { get; }

	public double Max { get; }

	public double Initial { get; }

	public ParameterScale Scale { get; }

	public bool AffectsManning => false;

	public IReadOnlyList<string> ProducesKeys => new string[1] { ContextKey(_coefficient) };

	public IReadOnlyList<string> RequiresKeys => Array.Empty<string>();

	internal static string ContextKey(TurbulenceCoefficient coefficient)
	{
		return $"turbulence:{coefficient}";
	}

	public TurbulenceParameter(TurbulenceCoefficient coefficient, double min, double max, double initial, string name = null, ParameterScale scale = ParameterScale.Linear)
	{
		if (max < min)
		{
			throw new ArgumentException("Max must be >= Min.");
		}
		if (initial < min || initial > max)
		{
			throw new ArgumentException("Initial value must be within [Min, Max].");
		}
		if (scale == ParameterScale.Log && min <= 0.0)
		{
			throw new ArgumentException("Log scale requires Min > 0.");
		}
		_coefficient = coefficient;
		Min = min;
		Max = max;
		Initial = initial;
		Scale = scale;
		Name = (string.IsNullOrEmpty(name) ? coefficient.ToString() : name);
	}

	public void Apply(double doubleValue, CalibrationContext ctx)
	{
		switch (_coefficient)
		{
		case TurbulenceCoefficient.BaseEddyViscosity:
			ctx.Turbulence.BaseEddyViscosity = doubleValue;
			return;
		case TurbulenceCoefficient.LongitudinalMixing:
			ctx.Turbulence.LongMixCoeff = doubleValue;
			return;
		case TurbulenceCoefficient.TransverseMixing:
			ctx.Turbulence.TransMixCoeff = doubleValue;
			return;
		case TurbulenceCoefficient.HorizontalMixing:
			ctx.Turbulence.HorizMixCoeff = doubleValue;
			return;
		}
		throw new NotImplementedException($"Unhandled turbulence coefficient {_coefficient}.");
	}
}
