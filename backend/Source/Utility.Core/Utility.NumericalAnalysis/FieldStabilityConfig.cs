using System.Numerics;

namespace Utility.NumericalAnalysis;

public readonly struct FieldStabilityConfig<T> where T : INumber<T>
{
	private readonly T _oscFloor;

	private readonly T _oscRelative;

	private readonly T _oscErrorFactor;

	private readonly T _resFloor;

	private readonly T _resRelative;

	private readonly T _resErrorFactor;

	private readonly T _scale;

	public double OscPowAlpha { get; }

	public double ResidualAlpha { get; }

	public EmaOrder Order { get; }

	public T Scale => _scale;

	public int SustainSteps { get; }

	public int MinSamples { get; }

	public bool CheckNonFinite { get; }

	public int MaxFlagged { get; }

	public FieldStabilityConfig(double oscPowAlpha, double residualAlpha, EmaOrder order, double oscFloor, double oscRelative, double oscErrorFactor, double resFloor, double resRelative, double resErrorFactor, int sustainSteps = 6, int minSamples = 8, double scale = 0.0, bool checkNonFinite = true, int maxFlagged = 32)
	{
		OscPowAlpha = oscPowAlpha;
		ResidualAlpha = residualAlpha;
		Order = order;
		_oscFloor = T.CreateChecked(oscFloor);
		_oscRelative = T.CreateChecked(oscRelative);
		_oscErrorFactor = T.CreateChecked(oscErrorFactor);
		_resFloor = T.CreateChecked(resFloor);
		_resRelative = T.CreateChecked(resRelative);
		_resErrorFactor = T.CreateChecked(resErrorFactor);
		_scale = T.CreateChecked(scale);
		SustainSteps = sustainSteps;
		MinSamples = minSamples;
		CheckNonFinite = checkNonFinite;
		MaxFlagged = maxFlagged;
	}

	public T OscWarnTol(T scale)
	{
		return T.Max(_oscFloor, _oscRelative * T.Max(T.Zero, scale));
	}

	public T OscErrorTol(T scale)
	{
		return OscWarnTol(scale) * _oscErrorFactor;
	}

	public T ResWarnTol(T scale)
	{
		return T.Max(_resFloor, _resRelative * T.Max(T.Zero, scale));
	}

	public T ResErrorTol(T scale)
	{
		return ResWarnTol(scale) * _resErrorFactor;
	}

	public static FieldStabilityConfig<T> ForStage()
	{
		return new FieldStabilityConfig<T>(0.3, 0.3, EmaOrder.Double, 0.05, 0.02, 2.0, 0.1, 0.1, 2.0);
	}

	public static FieldStabilityConfig<T> ForVelocity()
	{
		return new FieldStabilityConfig<T>(0.3, 0.3, EmaOrder.Double, 0.05, 0.02, 2.0, 0.2, 0.1, 2.0);
	}
}
