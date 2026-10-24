using Geospatial.Vectors;

namespace Ras.Synthetics;

public readonly record struct MeasuredStageSample(Point Location, double Time, double Stage);
