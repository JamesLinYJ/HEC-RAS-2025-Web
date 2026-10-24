using Geospatial.Vectors;

namespace Ras.Synthetics;

public readonly record struct MeasuredVelocitySample(Point Location, double Time, Vector Velocity);
