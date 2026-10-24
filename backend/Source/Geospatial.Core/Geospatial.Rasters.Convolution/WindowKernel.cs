using System;

namespace Geospatial.Rasters.Convolution;

public delegate TOut WindowKernel<TIn, TOut>(Span<TIn> data) where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged;
