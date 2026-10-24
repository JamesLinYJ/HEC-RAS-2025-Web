using System;
using System.Numerics;
using Geospatial.Rasters;

namespace Geospatial.Transformers;

public class RoundValueTransformer<T> : IValueTransformer<T> where T : unmanaged, IBinaryFloatingPointIeee754<T>
{
	private readonly Action<Memory<T>, T?> RoundAction;

	public static RoundValueTransformer<T> FromRoundTo(RoundTo rounding)
	{
		(RoundBase, int) tuple = RoundingConverters.Convert(rounding);
		return new RoundValueTransformer<T>(tuple.Item1, tuple.Item2);
	}

	public RoundValueTransformer(RoundBase round, int nDigits)
	{
		switch (round)
		{
		case RoundBase.Base2:
			RoundAction = (Memory<T> data, T? ndv) =>
			{
				if (ndv.HasValue)
				{
					RoundBase2(data, ndv.Value, nDigits);
				}
				else
				{
					RoundBase2(data, nDigits);
				}
			};
			break;
		case RoundBase.Base10:
			RoundAction = (Memory<T> data, T? ndv) =>
			{
				if (ndv.HasValue)
				{
					RoundBase10(data, ndv.Value, nDigits);
				}
				else
				{
					RoundBase10(data, nDigits);
				}
			};
			break;
		default:
			throw new NotSupportedException();
		}
	}

	public void Transform(Memory<T> data, T? noDataValue)
	{
		RoundAction(data, noDataValue);
	}

	private static void RoundBase10(Memory<T> data, T ndv, int nDigits)
	{
		Span<T> buffer = data.Span;
		for (int i = 0; i < buffer.Length; i++)
		{
			T localValue = buffer[i];
			if (!(localValue == ndv))
			{
				buffer[i] = T.Round(localValue, nDigits);
			}
		}
	}

	private static void RoundBase10(Memory<T> data, int nDigits)
	{
		Span<T> buffer = data.Span;
		for (int i = 0; i < buffer.Length; i++)
		{
			buffer[i] = T.Round(buffer[i], nDigits);
		}
	}

	private static void RoundBase2(Memory<T> data, T ndv, int nDigits)
	{
		Span<T> buffer = data.Span;
		T truncating = T.CreateTruncating(1 << nDigits);
		for (int i = 0; i < buffer.Length; i++)
		{
			T localValue = buffer[i];
			if (!(localValue == ndv))
			{
				buffer[i] = T.Round(localValue * truncating) / truncating;
			}
		}
	}

	private static void RoundBase2(Memory<T> data, int nDigits)
	{
		Span<T> buffer = data.Span;
		T truncating = T.CreateTruncating(1 << nDigits);
		for (int i = 0; i < buffer.Length; i++)
		{
			buffer[i] = T.Round(buffer[i] * truncating) / truncating;
		}
	}
}
