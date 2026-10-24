using System;

namespace Geospatial.Rasters.Convolution;

public delegate TOut SelfKernel<TIn, TOut>(TIn from) where TIn : unmanaged, IEquatable<TIn>;
