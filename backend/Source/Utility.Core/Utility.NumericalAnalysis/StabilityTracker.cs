using System;
using System.Numerics;

namespace Utility.NumericalAnalysis;

public sealed class StabilityTracker<T> where T : INumber<T>
{
	private readonly StabilityConfig<T> _cfg;

	private readonly TimeValueRingBuffer<T> _buffer;

	public StabilityState Last { get; private set; }

	public StabilityState WorstState { get; private set; }

	public StabilityResult<T> LastResult { get; private set; }

	public Action<StabilityResult<T>> Escalated { get; set; }

	public StabilityConfig<T> Config => _cfg;

	public TimeValueRingBuffer<T> Buffer => _buffer;

	public StabilityTracker(StabilityConfig<T> cfg)
	{
		_cfg = cfg;
		_buffer = new TimeValueRingBuffer<T>(cfg.Capacity, cfg.TimeCap);
	}

	public StabilityResult<T> Add(double time, T localValue)
	{
		return AddCore(time, localValue, hasPhysical: false, default, default);
	}

	public StabilityResult<T> Add(double time, T localValue, T depth, T velocity)
	{
		return AddCore(time, localValue, hasPhysical: true, depth, velocity);
	}

	private StabilityResult<T> AddCore(double time, T localValue, bool hasPhysical, T depth, T velocity)
	{
		_buffer.Add(time, localValue);
		StabilityResult<T> stabilityResult = StabilityAnalysis<T>.EvaluateSeries(_buffer, in _cfg);
		StabilityState stabilityState = StabilityState.Stable;
		InstabilityKind instabilityKind = InstabilityKind.None;
		if (_cfg.CheckNonFinite && (!T.IsFinite(localValue) || (hasPhysical && (!T.IsFinite(depth) || !T.IsFinite(velocity)))))
		{
			stabilityState = StabilityState.Error;
			instabilityKind |= InstabilityKind.NonFinite;
		}
		if (hasPhysical && _cfg.CheckNegativeDepth && T.IsFinite(depth) && depth < T.Zero)
		{
			stabilityState = StabilityState.Error;
			instabilityKind |= InstabilityKind.NegativeDepth;
		}
		if (hasPhysical && _cfg.FroudeCap > 0.0 && T.IsFinite(depth) && T.IsFinite(velocity))
		{
			double depthTerm = StabilityAnalysis<T>.D(depth);
			double velocityTerm = StabilityAnalysis<T>.D(velocity);
			if (depthTerm > 0.0 && velocityTerm * velocityTerm > _cfg.FroudeCap * _cfg.FroudeCap * _cfg.Gravity * depthTerm)
			{
				stabilityState = StabilityAnalysis<T>.Max(stabilityState, StabilityState.Warning);
				instabilityKind |= InstabilityKind.FroudeExceeded;
			}
		}
		StabilityState stabilityState2 = StabilityAnalysis<T>.Max(stabilityResult.State, stabilityState);
		InstabilityKind kind = stabilityResult.Kind | instabilityKind;
		StabilityResult<T> stabilityResult2 = new StabilityResult<T>(stabilityState2, kind, stabilityResult.Time, localValue, stabilityResult.OscSteps, stabilityResult.Delta, stabilityResult.GrowthRatio);
		StabilityState last = Last;
		Last = stabilityState2;
		LastResult = stabilityResult2;
		if (stabilityState2 > WorstState)
		{
			WorstState = stabilityState2;
		}
		if (stabilityState2 > last && stabilityState2 >= StabilityState.Warning)
		{
			Escalated?.Invoke(stabilityResult2);
		}
		return stabilityResult2;
	}
}
