using System;
using Geospatial.Vectors;

namespace Ras.Optimization;

public sealed class Gauge
{
	public string Name { get; }

	public GaugeVariable Variable { get; }

	public Point Location { get; }

	public Polyline CrossSection { get; }

	public ObservedSeries Observed { get; }

	public double Weight { get; }

	public IObjectiveMetric Metric { get; }

	private Gauge(string name, GaugeVariable variable, Point location, Polyline crossSection, ObservedSeries observed, IObjectiveMetric metric, double weight)
	{
		if (observed == null)
		{
			throw new ArgumentNullException("observed");
		}
		if (metric == null)
		{
			throw new ArgumentNullException("metric");
		}
		if (weight <= 0.0)
		{
			throw new ArgumentException("Gauge weight must be positive.", "weight");
		}
		Name = name;
		Variable = variable;
		Location = location;
		CrossSection = crossSection;
		Observed = observed;
		Metric = metric;
		Weight = weight;
	}

	public static Gauge ForStage(string name, Point location, ObservedSeries observed, IObjectiveMetric metric, double weight = 1.0)
	{
		return new Gauge(name, GaugeVariable.WaterSurfaceElevation, location, null, observed, metric, weight);
	}

	public static Gauge ForVelocity(string name, Point location, ObservedSeries observed, IObjectiveMetric metric, double weight = 1.0)
	{
		return new Gauge(name, GaugeVariable.Velocity, location, null, observed, metric, weight);
	}

	public static Gauge ForDischarge(string name, Polyline crossSection, ObservedSeries observed, IObjectiveMetric metric, double weight = 1.0)
	{
		if (crossSection == null)
		{
			throw new ArgumentNullException("crossSection");
		}
		return new Gauge(name, GaugeVariable.Discharge, default, crossSection, observed, metric, weight);
	}
}
