using System;
using Geospatial.Vectors;

namespace Visual.Mapping.Observables;

public interface IVectorContext<TFeature> : IVectorFeatureContext where TFeature : ICloneable
{
	IVectorCollection<TFeature> Features { get; }
}
