using System;

namespace Ras.Synthetics.Analysis;

public class CellSteadyMonitor
{
	private readonly SteadyMonitor _depth;

	private readonly SteadyMonitor _velocity;

	private readonly double _dryDepth;

	public SteadyMonitor Depth => _depth;

	public SteadyMonitor Velocity => _velocity;

	public SteadyState State
	{
		get
		{
			if (_depth.WindowMax() < _dryDepth)
			{
				return SteadyState.Dry;
			}
			if (_depth.IsSteady && _velocity.IsSteady)
			{
				return SteadyState.True;
			}
			return SteadyState.False;
		}
	}

	public double OnsetTime
	{
		get
		{
			if (State != SteadyState.True)
			{
				return double.NaN;
			}
			return Math.Max(_depth.SteadyTime, _velocity.SteadyTime);
		}
	}

	public CellSteadyMonitor(SteadyConfig depthCfg, SteadyConfig velCfg)
	{
		_depth = new SteadyMonitor(depthCfg);
		_velocity = new SteadyMonitor(velCfg);
		_dryDepth = depthCfg.DryDepth;
	}

	public void Add(double time, double depth, double velocity)
	{
		_depth.Add(time, depth);
		_velocity.Add(time, velocity);
	}
}
