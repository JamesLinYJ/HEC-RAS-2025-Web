using System;

namespace Geospatial.Rasters.Convolution;

public delegate TOut NeighborKernel<TIn, TOut>(TIn from, TIn to) where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged;
