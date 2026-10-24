using System;
using System.Collections.Generic;
using Ras.Engine;

namespace Ras.Optimization;

public sealed class TurbulenceRatioParameter : ICalibrationParameter
{
	private readonly TurbulenceCoefficient _target;

	private readonly TurbulenceCoefficient _reference;

	public string Name { get; }

	public double Min { get; }

	public double Max { get; }

	public double Initial { get; }

	public ParameterScale Scale { get; }

	public bool AffectsManning => false;

	public IReadOnlyList<string> ProducesKeys => new string[1] { TurbulenceParameter.ContextKey(_target) };

	public IReadOnlyList<string> RequiresKeys => new string[1] { TurbulenceParameter.ContextKey(_reference) };

	public TurbulenceRatioParameter(TurbulenceCoefficient target, TurbulenceCoefficient reference, double minRatio, double maxRatio, double initialRatio, string name = null, ParameterScale scale = ParameterScale.Linear)
	{
		if (target == reference)
		{
			throw new ArgumentException("Target and reference coefficients must differ.");
		}
		if (maxRatio < minRatio)
		{
			throw new ArgumentException("Max ratio must be >= Min ratio.");
		}
		if (initialRatio < minRatio || initialRatio > maxRatio)
		{
			throw new ArgumentException("Initial ratio must be within [Min, Max].");
		}
		if (scale == ParameterScale.Log && minRatio <= 0.0)
		{
			throw new ArgumentException("Log scale requires Min > 0.");
		}
		_target = target;
		_reference = reference;
		Min = minRatio;
		Max = maxRatio;
		Initial = initialRatio;
		Scale = scale;
		Name = (string.IsNullOrEmpty(name) ? $"{target}/{reference}" : name);
	}

	public void Apply(double ratio, CalibrationContext ctx)
	{
		double coefficient = ReadCoefficient(ctx.Turbulence, _reference);
		SetCoefficient(ctx, _target, ratio * coefficient);
	}

	private static double ReadCoefficient(TurbulenceParams turbulence, TurbulenceCoefficient coefficient)
	{
		return coefficient switch
		{
			TurbulenceCoefficient.BaseEddyViscosity => turbulence.BaseEddyViscosity, 
			TurbulenceCoefficient.LongitudinalMixing => turbulence.LongMixCoeff, 
			TurbulenceCoefficient.TransverseMixing => turbulence.TransMixCoeff, 
			TurbulenceCoefficient.HorizontalMixing => turbulence.HorizMixCoeff, 
			_ => throw new NotImplementedException($"Unhandled turbulence coefficient {coefficient}."), 
		};
	}

	private static void SetCoefficient(CalibrationContext ctx, TurbulenceCoefficient coefficient, double computed)
	{
		switch (coefficient)
		{
		case TurbulenceCoefficient.BaseEddyViscosity:
			ctx.Turbulence.BaseEddyViscosity = computed;
			return;
		case TurbulenceCoefficient.LongitudinalMixing:
			ctx.Turbulence.LongMixCoeff = computed;
			return;
		case TurbulenceCoefficient.TransverseMixing:
			ctx.Turbulence.TransMixCoeff = computed;
			return;
		case TurbulenceCoefficient.HorizontalMixing:
			ctx.Turbulence.HorizMixCoeff = computed;
			return;
		}
		throw new NotImplementedException($"Unhandled turbulence coefficient {coefficient}.");
	}
}
