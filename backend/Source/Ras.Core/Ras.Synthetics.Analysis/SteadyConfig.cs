using System;

namespace Ras.Synthetics.Analysis;

public readonly struct SteadyConfig
{
	public double Window { get; }

	public double BandFloor { get; }

	public double BandRelative { get; }

	public double DriftFloor { get; }

	public double DriftRelative { get; }

	public double DryDepth { get; }

	public double Scale { get; }

	public int MinSamplesPerWindow { get; }

	public SteadyConfig(double window, double bandFloor, double bandRelative, double driftFloor, double driftRelative, double dryDepth = 0.001, double scale = 0.0, int minSamplesPerWindow = 4)
	{
		Window = window;
		BandFloor = bandFloor;
		BandRelative = bandRelative;
		DriftFloor = driftFloor;
		DriftRelative = driftRelative;
		DryDepth = dryDepth;
		Scale = scale;
		MinSamplesPerWindow = minSamplesPerWindow;
	}

	public static SteadyConfig ForDepth(double window)
	{
		return new SteadyConfig(window, 0.003, 0.01, 0.002, 0.005);
	}

	public static SteadyConfig ForVelocity(double window)
	{
		return new SteadyConfig(window, 0.01, 0.01, 0.005, 0.005);
	}

	public double BandTol(double scale)
	{
		return Math.Max(BandFloor, BandRelative * Math.Max(0.0, scale));
	}

	public double DriftTol(double scale)
	{
		return Math.Max(DriftFloor, DriftRelative * Math.Max(0.0, scale));
	}
}
