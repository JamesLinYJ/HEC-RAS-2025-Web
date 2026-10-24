using System.Numerics;

namespace Utility.NumericalAnalysis;

public readonly struct StabilityConfig<T> where T : INumber<T>
{
	private readonly T _magFloor;

	private readonly T _magRelative;

	private readonly T _magErrorFactor;

	private readonly T _deadbandFloor;

	private readonly T _deadbandRelative;

	private readonly T _scale;

	public int Capacity { get; }

	public double TimeCap { get; }

	public int OscWarnSteps { get; }

	public int OscErrorSteps { get; }

	public T MagErrorFactor => _magErrorFactor;

	public T Scale => _scale;

	public double GrowthFactor { get; }

	public int GrowthCycles { get; }

	public int MinSamples { get; }

	public bool CheckNonFinite { get; }

	public bool CheckNegativeDepth { get; }

	public double FroudeCap { get; }

	public double Gravity { get; }

	public StabilityConfig(int capacity, double timeCap, int oscWarnSteps, int oscErrorSteps, double magFloor, double magRelative, double magErrorFactor, double growthFactor, int growthCycles, double deadbandFloor, double deadbandRelative, double scale = 0.0, int minSamples = 3, bool checkNonFinite = true, bool checkNegativeDepth = true, double froudeCap = 0.0, double gravity = 9.80665)
	{
		Capacity = capacity;
		TimeCap = timeCap;
		OscWarnSteps = oscWarnSteps;
		OscErrorSteps = oscErrorSteps;
		_magFloor = T.CreateChecked(magFloor);
		_magRelative = T.CreateChecked(magRelative);
		_magErrorFactor = T.CreateChecked(magErrorFactor);
		_deadbandFloor = T.CreateChecked(deadbandFloor);
		_deadbandRelative = T.CreateChecked(deadbandRelative);
		_scale = T.CreateChecked(scale);
		GrowthFactor = growthFactor;
		GrowthCycles = growthCycles;
		MinSamples = minSamples;
		CheckNonFinite = checkNonFinite;
		CheckNegativeDepth = checkNegativeDepth;
		FroudeCap = froudeCap;
		Gravity = gravity;
	}

	public T MagWarnTol(T scale)
	{
		return T.Max(_magFloor, _magRelative * T.Max(T.Zero, scale));
	}

	public T MagErrorTol(T scale)
	{
		return MagWarnTol(scale) * _magErrorFactor;
	}

	public T Deadband(T scale)
	{
		return T.Max(_deadbandFloor, _deadbandRelative * T.Max(T.Zero, scale));
	}

	public static StabilityConfig<T> ForStage(int capacity = 64, double timeCap = double.PositiveInfinity)
	{
		return new StabilityConfig<T>(capacity, timeCap, 6, 12, 0.5, 0.25, 2.0, 1.5, 3, 0.01, 0.02);
	}

	public static StabilityConfig<T> ForVelocity(int capacity = 64, double timeCap = double.PositiveInfinity)
	{
		return new StabilityConfig<T>(capacity, timeCap, 6, 12, 1.0, 0.25, 2.0, 1.5, 3, 0.02, 0.02, 0.0, 3, checkNonFinite: true, checkNegativeDepth: false);
	}
}
