using System;

namespace Geospatial.Rasters;

public static class ConverterDelegates
{
	public delegate void ConvertInPlace<T>(Memory<T> inputOutput);

	public delegate void ConvertInPlaceNDV<T>(Memory<T> inputOutput, T? NDV) where T : unmanaged;

	public delegate void Convert<T>(Memory<T> input, Memory<T> output);

	public delegate void ConvertNDV<T>(Memory<T> input, Memory<T> output, T? inputNDV, T? outputNDV) where T : unmanaged;

	public delegate void ConvertTo<TIn, TOut>(Memory<TIn> input, Memory<TOut> output);

	public delegate void ConvertToNDV<TIn, TOut>(Memory<TIn> input, Memory<TOut> output, TIn? inputNDV, TOut? outputNDV) where TIn : unmanaged where TOut : unmanaged;
}
