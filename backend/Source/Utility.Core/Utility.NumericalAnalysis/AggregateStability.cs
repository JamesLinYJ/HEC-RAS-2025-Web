using System;
using System.Numerics;

namespace Utility.NumericalAnalysis;

public sealed class AggregateStability<T> where T : INumber<T>
{
	private readonly StabilityConfig<T> _cfg;

	private readonly int _count;

	private readonly T[] _prev;

	private readonly sbyte[] _prevSign;

	private readonly int[] _oscRun;

	private double _lastTime = double.NaN;

	private bool _primed;

	private int _steps;

	public StabilityState Last { get; private set; }

	public StabilityState WorstState { get; private set; }

	public AggregateStabilityResult<T> LastResult { get; private set; }

	public Action<AggregateStabilityResult<T>> Escalated { get; set; }

	public StabilityConfig<T> Config => _cfg;

	public AggregateStability(int elementCount, StabilityConfig<T> cfg)
	{
		if (elementCount < 1)
		{
			throw new ArgumentOutOfRangeException("elementCount");
		}
		_cfg = cfg;
		_count = elementCount;
		_prev = new T[elementCount];
		_prevSign = new sbyte[elementCount];
		_oscRun = new int[elementCount];
	}

	public AggregateStabilityResult<T> Add(double time, ReadOnlySpan<T> values)
	{
		if (values.Length != _count)
		{
			throw new ArgumentException("Values length must match the element count.", "values");
		}
		bool notIsNaN = !double.IsNaN(_lastTime) && time <= _lastTime;
		if (!_primed | notIsNaN)
		{
			return Prime(time, values);
		}
		_steps++;
		T scale = ((_cfg.Scale > T.Zero) ? _cfg.Scale : SpatialSpan(values));
		T magWarnTol = _cfg.MagWarnTol(scale);
		T magErrorTol = _cfg.MagErrorTol(scale);
		T deadband = _cfg.Deadband(scale);
		T unknownIds = T.Zero;
		int ordinal = -1;
		int unknownIds2 = 0;
		int ordinal2 = -1;
		int unknownIds3 = 0;
		int ordinal3 = -1;
		for (int i = 0; i < _count; i++)
		{
			T prev = values[i];
			if (_cfg.CheckNonFinite && !T.IsFinite(prev))
			{
				if (ordinal3 < 0)
				{
					ordinal3 = i;
				}
				_prev[i] = prev;
				continue;
			}
			T prevTerm = prev - _prev[i];
			T unknownIds4 = T.Abs(prevTerm);
			if (unknownIds4 > unknownIds)
			{
				unknownIds = unknownIds4;
				ordinal = i;
			}
			int prevSign = StabilityAnalysis<T>.SignOf(prevTerm, deadband);
			if (prevSign != 0)
			{
				if (_prevSign[i] != 0 && prevSign == -_prevSign[i])
				{
					_oscRun[i]++;
				}
				else
				{
					_oscRun[i] = 0;
				}
				_prevSign[i] = (sbyte)prevSign;
			}
			if (_oscRun[i] >= _cfg.OscWarnSteps)
			{
				unknownIds3++;
			}
			if (_oscRun[i] > unknownIds2)
			{
				unknownIds2 = _oscRun[i];
				ordinal2 = i;
			}
			_prev[i] = prev;
		}
		if (_steps < _cfg.MinSamples)
		{
			_lastTime = time;
			Last = StabilityState.Unknown;
			return LastResult = new AggregateStabilityResult<T>(StabilityState.Unknown, InstabilityKind.None, time, -1, T.Zero, unknownIds, unknownIds2, unknownIds3);
		}
		StabilityState stabilityState = ((unknownIds >= magErrorTol) ? StabilityState.Error : ((!(unknownIds >= magWarnTol)) ? StabilityState.Stable : StabilityState.Warning));
		StabilityState stabilityState2 = ((unknownIds2 >= _cfg.OscErrorSteps) ? StabilityState.Error : ((unknownIds2 < _cfg.OscWarnSteps) ? StabilityState.Stable : StabilityState.Warning));
		StabilityState stabilityState3 = StabilityState.Stable;
		InstabilityKind instabilityKind = InstabilityKind.None;
		int unity = -1;
		if (ordinal3 >= 0)
		{
			stabilityState3 = StabilityState.Error;
			instabilityKind |= InstabilityKind.NonFinite;
			unity = ordinal3;
		}
		if (stabilityState > stabilityState3 || (stabilityState == stabilityState3 && unity < 0))
		{
			stabilityState3 = StabilityAnalysis<T>.Max(stabilityState3, stabilityState);
			unity = ordinal;
		}
		if (stabilityState >= StabilityState.Warning)
		{
			instabilityKind |= InstabilityKind.Magnitude;
		}
		if (stabilityState2 > stabilityState3 || (stabilityState2 == stabilityState3 && (instabilityKind & InstabilityKind.NonFinite) == 0 && unity < 0))
		{
			stabilityState3 = StabilityAnalysis<T>.Max(stabilityState3, stabilityState2);
			unity = ordinal2;
		}
		if (stabilityState2 >= StabilityState.Warning)
		{
			instabilityKind |= InstabilityKind.Oscillation;
		}
		T worstValue = ((unity >= 0) ? values[unity] : T.Zero);
		AggregateStabilityResult<T> aggregateStabilityResult2 = new AggregateStabilityResult<T>(stabilityState3, instabilityKind, time, unity, worstValue, unknownIds, unknownIds2, unknownIds3);
		StabilityState last = Last;
		Last = stabilityState3;
		LastResult = aggregateStabilityResult2;
		if (stabilityState3 > WorstState)
		{
			WorstState = stabilityState3;
		}
		_lastTime = time;
		if (stabilityState3 > last && stabilityState3 >= StabilityState.Warning)
		{
			Escalated?.Invoke(aggregateStabilityResult2);
		}
		return aggregateStabilityResult2;
	}

	private AggregateStabilityResult<T> Prime(double time, ReadOnlySpan<T> values)
	{
		for (int i = 0; i < _count; i++)
		{
			_prev[i] = values[i];
			_prevSign[i] = 0;
			_oscRun[i] = 0;
		}
		_primed = true;
		_steps = 0;
		_lastTime = time;
		Last = StabilityState.Unknown;
		return LastResult = new AggregateStabilityResult<T>(StabilityState.Unknown, InstabilityKind.None, time, -1, T.Zero, T.Zero, 0, 0);
	}

	private static T SpatialSpan(ReadOnlySpan<T> values)
	{
		bool condition = false;
		T zero = T.Zero;
		T zero2 = T.Zero;
		for (int i = 0; i < values.Length; i++)
		{
			T localValue = values[i];
			if (!T.IsFinite(localValue))
			{
				continue;
			}
			if (!condition)
			{
				zero = localValue;
				zero2 = localValue;
				condition = true;
				continue;
			}
			if (localValue < zero)
			{
				zero = localValue;
			}
			if (localValue > zero2)
			{
				zero2 = localValue;
			}
		}
		if (!condition)
		{
			return T.Zero;
		}
		return zero2 - zero;
	}
}
