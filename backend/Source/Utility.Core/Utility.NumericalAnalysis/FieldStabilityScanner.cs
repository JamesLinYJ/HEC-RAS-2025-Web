using System;
using System.Collections.Generic;
using System.Numerics;

namespace Utility.NumericalAnalysis;

public sealed class FieldStabilityScanner<T> where T : INumber<T>
{
	private readonly FieldStabilityConfig<T> _cfg;

	private readonly int _count;

	private readonly T[] _prev1;

	private readonly T[] _prev2;

	private readonly T[] _oscPow;

	private readonly T[] _ema1;

	private readonly T[] _ema2;

	private readonly T[] _ema3;

	private readonly int[] _sustain;

	private readonly bool[] _isFlagged;

	private int[] _recordSlot;

	private List<FieldSample>[] _recordSeries;

	private readonly T _ap;

	private readonly T _oneMinusAp;

	private readonly T _ar;

	private readonly T _oneMinusAr;

	private readonly T _two = T.CreateChecked(2);

	private readonly T _three = T.CreateChecked(3);

	private readonly List<int> _flagged = new List<int>();

	private double _lastTime = double.NaN;

	private bool _primed;

	private int _steps;

	public StabilityState WorstState { get; private set; }

	public FieldStabilityResult LastWorst { get; private set; } = FieldStabilityResult.Unknown(double.NaN);

	public IReadOnlyList<int> FlaggedElements => _flagged;

	public Action<FieldStabilityResult> Escalated { get; set; }

	public FieldStabilityConfig<T> Config => _cfg;

	public FieldStabilityScanner(int elementCount, FieldStabilityConfig<T> cfg)
	{
		if (elementCount < 1)
		{
			throw new ArgumentOutOfRangeException("elementCount");
		}
		_cfg = cfg;
		_count = elementCount;
		_prev1 = new T[elementCount];
		_prev2 = new T[elementCount];
		_oscPow = new T[elementCount];
		_ema1 = new T[elementCount];
		_ema2 = new T[elementCount];
		_ema3 = new T[elementCount];
		_sustain = new int[elementCount];
		_isFlagged = new bool[elementCount];
		_ap = T.CreateChecked(cfg.OscPowAlpha);
		_oneMinusAp = T.CreateChecked(1.0 - cfg.OscPowAlpha);
		_ar = T.CreateChecked(cfg.ResidualAlpha);
		_oneMinusAr = T.CreateChecked(1.0 - cfg.ResidualAlpha);
	}

	public void Record(IReadOnlyList<int> denseIndices, IReadOnlyList<List<FieldSample>> series)
	{
		if (denseIndices.Count != series.Count)
		{
			throw new ArgumentException("Index and series counts must match.", "series");
		}
		_recordSlot = new int[_count];
		for (int i = 0; i < _count; i++)
		{
			_recordSlot[i] = -1;
		}
		_recordSeries = new List<FieldSample>[denseIndices.Count];
		for (int j = 0; j < denseIndices.Count; j++)
		{
			int denseIndex = denseIndices[j];
			if ((uint)denseIndex >= (uint)_count)
			{
				throw new ArgumentOutOfRangeException("denseIndices", $"Index {denseIndex} is outside [0, {_count}).");
			}
			if (series[j] == null)
			{
				throw new ArgumentNullException("series", $"Series for index {denseIndex} is null.");
			}
			_recordSlot[denseIndex] = j;
			_recordSeries[j] = series[j];
		}
	}

	public FieldStabilityResult Add(double time, ReadOnlySpan<T> values)
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
		T oscWarnTol = _cfg.OscWarnTol(scale);
		T oscErrorTol = _cfg.OscErrorTol(scale);
		T oscWarnTolPolynomial = oscWarnTol * oscWarnTol;
		T oscErrorTolPolynomial = oscErrorTol * oscErrorTol;
		T resWarnTol = _cfg.ResWarnTol(scale);
		T resErrorTol = _cfg.ResErrorTol(scale);
		bool stepsMinSamplesTerm = _steps >= _cfg.MinSamples;
		StabilityState stabilityState = StabilityState.Stable;
		InstabilityKind kind = InstabilityKind.None;
		int element = -1;
		double createChecked = 0.0;
		double oscPower = 0.0;
		double residual = 0.0;
		for (int i = 0; i < _count; i++)
		{
			T prev1 = values[i];
			if (_cfg.CheckNonFinite && !T.IsFinite(prev1))
			{
				if (stepsMinSamplesTerm && stabilityState < StabilityState.Error)
				{
					stabilityState = StabilityState.Error;
					kind = InstabilityKind.NonFinite;
					element = i;
					createChecked = double.CreateChecked(prev1);
					oscPower = double.CreateChecked(_oscPow[i]);
					residual = 0.0;
				}
				if (stepsMinSamplesTerm)
				{
					Flag(i, time, prev1, StabilityState.Error, InstabilityKind.NonFinite, _oscPow[i], T.Zero);
				}
				Capture(i, time, prev1, _oscPow[i], T.Zero, StabilityState.Error);
				continue;
			}
			T prev1TwoTerm = prev1 - _two * _prev1[i] + _prev2[i];
			T oscPow = _oneMinusAp * _oscPow[i] + _ap * (prev1TwoTerm * prev1TwoTerm);
			_oscPow[i] = oscPow;
			_prev2[i] = _prev1[i];
			_prev1[i] = prev1;
			_ema1[i] = _oneMinusAr * _ema1[i] + _ar * prev1;
			_ema2[i] = _oneMinusAr * _ema2[i] + _ar * _ema1[i];
			_ema3[i] = _oneMinusAr * _ema3[i] + _ar * _ema2[i];
			T prev1OrderTerm = prev1 - _cfg.Order switch
			{
				EmaOrder.Triple => _three * _ema1[i] - _three * _ema2[i] + _ema3[i], 
				EmaOrder.Double => _two * _ema1[i] - _ema2[i], 
				_ => _ema1[i], 
			};
			T localValue = T.Abs(prev1OrderTerm);
			StabilityState stabilityState2 = ((oscPow >= oscErrorTolPolynomial) ? StabilityState.Error : ((!(oscPow >= oscWarnTolPolynomial)) ? StabilityState.Stable : StabilityState.Warning));
			StabilityState stabilityState3 = ((localValue >= resErrorTol) ? StabilityState.Error : ((!(localValue >= resWarnTol)) ? StabilityState.Stable : StabilityState.Warning));
			StabilityState stabilityState4 = ((stabilityState2 >= stabilityState3) ? stabilityState2 : stabilityState3);
			Capture(i, time, prev1, oscPow, prev1OrderTerm, stabilityState4);
			if (stabilityState4 >= StabilityState.Warning)
			{
				_sustain[i]++;
			}
			else
			{
				_sustain[i] = 0;
			}
			if (stepsMinSamplesTerm && _sustain[i] >= _cfg.SustainSteps)
			{
				InstabilityKind instabilityKind = InstabilityKind.None;
				if (stabilityState2 >= StabilityState.Warning)
				{
					instabilityKind |= InstabilityKind.Oscillation;
				}
				if (stabilityState3 >= StabilityState.Warning)
				{
					instabilityKind |= InstabilityKind.Magnitude;
				}
				Flag(i, time, prev1, stabilityState4, instabilityKind, oscPow, prev1OrderTerm);
				if (stabilityState4 > stabilityState)
				{
					stabilityState = stabilityState4;
					kind = instabilityKind;
					element = i;
					createChecked = double.CreateChecked(prev1);
					oscPower = double.CreateChecked(oscPow);
					residual = double.CreateChecked(prev1OrderTerm);
				}
			}
		}
		_lastTime = time;
		FieldStabilityResult result = (LastWorst = new FieldStabilityResult(stabilityState, kind, time, element, createChecked, oscPower, residual));
		if (stabilityState > WorstState)
		{
			WorstState = stabilityState;
		}
		return result;
	}

	private void Flag(int i, double time, T localValue, StabilityState state, InstabilityKind kind, T oscPow, T residual)
	{
		if (!_isFlagged[i] && _flagged.Count < _cfg.MaxFlagged)
		{
			_isFlagged[i] = true;
			_flagged.Add(i);
			Escalated?.Invoke(new FieldStabilityResult(state, kind, time, i, double.CreateChecked(localValue), double.CreateChecked(oscPow), double.CreateChecked(residual)));
		}
	}

	private void Capture(int i, double time, T localValue, T oscPow, T residual, StabilityState state)
	{
		if (_recordSlot != null)
		{
			int recordSlot = _recordSlot[i];
			if (recordSlot >= 0)
			{
				double oscRms = Math.Sqrt(Math.Max(0.0, double.CreateChecked(oscPow)));
				_recordSeries[recordSlot].Add(new FieldSample(time, double.CreateChecked(localValue), oscRms, double.CreateChecked(residual), state));
			}
		}
	}

	private FieldStabilityResult Prime(double time, ReadOnlySpan<T> values)
	{
		if (_recordSeries != null)
		{
			List<FieldSample>[] recordSeries = _recordSeries;
			foreach (List<FieldSample> recordSery in recordSeries)
			{
				int recordSeryCount = recordSery.Count;
				while (recordSeryCount > 0 && recordSery[recordSeryCount - 1].Time >= time)
				{
					recordSeryCount--;
				}
				if (recordSeryCount < recordSery.Count)
				{
					recordSery.RemoveRange(recordSeryCount, recordSery.Count - recordSeryCount);
				}
			}
		}
		for (int j = 0; j < _count; j++)
		{
			T zero = (T.IsFinite(values[j]) ? values[j] : T.Zero);
			_prev1[j] = zero;
			_prev2[j] = zero;
			_oscPow[j] = T.Zero;
			_ema1[j] = zero;
			_ema2[j] = zero;
			_ema3[j] = zero;
			_sustain[j] = 0;
		}
		_primed = true;
		_steps = 0;
		_lastTime = time;
		return LastWorst = FieldStabilityResult.Unknown(time);
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
