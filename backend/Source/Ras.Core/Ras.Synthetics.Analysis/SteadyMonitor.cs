using System;
using System.Collections.Generic;

namespace Ras.Synthetics.Analysis;

public class SteadyMonitor
{
	private readonly SteadyConfig _cfg;

	private readonly Queue<(double Time, double Value)> _window = new Queue<(double, double)>();

	private readonly List<(double Time, double Value)> _buffer = new List<(double, double)>();

	private double _firstTime = double.NaN;

	private double _globalMin = double.PositiveInfinity;

	private double _globalMax = double.NegativeInfinity;

	public bool IsSteady { get; private set; }

	public double SteadyTime { get; private set; } = double.NaN;

	public double LastBand { get; private set; } = double.NaN;

	public double LastDrift { get; private set; } = double.NaN;

	public double OnsetBand { get; private set; } = double.NaN;

	public double OnsetDrift { get; private set; } = double.NaN;

	public Action<double> SteadyReached { get; set; }

	public SteadyMonitor(SteadyConfig cfg)
	{
		_cfg = cfg;
	}

	public void Add(double time, double doubleValue)
	{
		if (!double.IsFinite(time) || !double.IsFinite(doubleValue))
		{
			return;
		}
		if (double.IsNaN(_firstTime))
		{
			_firstTime = time;
		}
		if (doubleValue < _globalMin)
		{
			_globalMin = doubleValue;
		}
		if (doubleValue > _globalMax)
		{
			_globalMax = doubleValue;
		}
		double window = _cfg.Window;
		_window.Enqueue((time, doubleValue));
		while (_window.Count > 0 && _window.Peek().Time < time - window)
		{
			_window.Dequeue();
		}
		double scale = ((_cfg.Scale > 0.0) ? _cfg.Scale : Math.Max(0.0, _globalMax - _globalMin));
		double bandTol = _cfg.BandTol(scale);
		double driftTol = _cfg.DriftTol(scale);
		_buffer.Clear();
		foreach (var window2 in _window)
		{
			_buffer.Add(window2);
		}
		if (time - _firstTime >= window && EvaluateBuffer(time, window, bandTol, driftTol))
		{
			if (!IsSteady)
			{
				IsSteady = true;
				SteadyTime = time - window;
				OnsetBand = LastBand;
				OnsetDrift = LastDrift;
				SteadyReached?.Invoke(SteadyTime);
			}
		}
		else
		{
			IsSteady = false;
			SteadyTime = double.NaN;
			OnsetBand = double.NaN;
			OnsetDrift = double.NaN;
		}
	}

	public double WindowMax()
	{
		double negativeInfinity = double.NegativeInfinity;
		foreach (var window in _window)
		{
			if (window.Value > negativeInfinity)
			{
				negativeInfinity = window.Value;
			}
		}
		return negativeInfinity;
	}

	private bool EvaluateBuffer(double tEnd, double window, double bandTol, double driftTol)
	{
		int count = _buffer.Count;
		if (count == 0)
		{
			return false;
		}
		double tEndWindowTerm = tEnd - window / 2.0;
		double positiveInfinity = double.PositiveInfinity;
		double negativeInfinity = double.NegativeInfinity;
		double computed = 0.0;
		double computed2 = 0.0;
		int ordinal = 0;
		int ordinal2 = 0;
		foreach (var localValue in _buffer)
		{
			if (localValue.Value < positiveInfinity)
			{
				positiveInfinity = localValue.Value;
			}
			if (localValue.Value > negativeInfinity)
			{
				negativeInfinity = localValue.Value;
			}
			if (localValue.Time <= tEndWindowTerm)
			{
				computed += localValue.Value;
				ordinal++;
			}
			else
			{
				computed2 += localValue.Value;
				ordinal2++;
			}
		}
		LastBand = negativeInfinity - positiveInfinity;
		bool condition = ordinal > 0 && ordinal2 > 0;
		LastDrift = (condition ? Math.Abs(computed2 / (double)ordinal2 - computed / (double)ordinal) : double.NaN);
		if (count < _cfg.MinSamplesPerWindow)
		{
			return false;
		}
		if (!condition)
		{
			return false;
		}
		if (LastBand < bandTol)
		{
			return LastDrift < driftTol;
		}
		return false;
	}
}
