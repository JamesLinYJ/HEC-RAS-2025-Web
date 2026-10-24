using System;
using System.Numerics;

namespace Utility.NumericalAnalysis;

public static class EndpointPolynomialFitter
{
	public static bool TryFitQuadratic<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, out EndpointPolynomialFit<T> fit) where T : IFloatingPointIeee754<T>
	{
		ValidateEndpoints(x, y);
		T localValue = x[0];
		T localValue2 = x[x.Length - 1] - localValue;
		T localValue3 = y[0];
		T localValue4 = y[y.Length - 1] - localValue3;
		T zero = T.Zero;
		T zero2 = T.Zero;
		T previousX = localValue;
		for (int i = 1; i < x.Length - 1; i++)
		{
			ValidateInteriorPoint(x[i], y[i], previousX);
			T localValue5 = (x[i] - localValue) / localValue2;
			T oneTerm = localValue5 * (localValue5 - T.One);
			T localValue6 = y[i] - (localValue3 + localValue4 * localValue5);
			zero += oneTerm * oneTerm;
			zero2 += oneTerm * localValue6;
			previousX = x[i];
		}
		ValidateLastX(x[x.Length - 1], previousX);
		T zeroIds = (IsUsableDenominator(zero) ? (zero2 / zero) : T.Zero);
		fit = new EndpointPolynomialFit<T>(zeroIds, T.Zero);
		if (T.IsFinite(zeroIds))
		{
			return IsMonotonic(localValue4, fit);
		}
		return false;
	}

	public static bool TryFitCubic<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, out EndpointPolynomialFit<T> fit) where T : IFloatingPointIeee754<T>
	{
		ValidateEndpoints(x, y);
		T localValue = x[0];
		T localValue2 = x[x.Length - 1] - localValue;
		T localValue3 = y[0];
		T localValue4 = y[y.Length - 1] - localValue3;
		T zero = T.Zero;
		T zero2 = T.Zero;
		T zero3 = T.Zero;
		T zero4 = T.Zero;
		T zero5 = T.Zero;
		T previousX = localValue;
		int lengthOffset = x.Length - 2;
		for (int i = 1; i < x.Length - 1; i++)
		{
			ValidateInteriorPoint(x[i], y[i], previousX);
			T localValue5 = (x[i] - localValue) / localValue2;
			T oneTerm = localValue5 * (localValue5 - T.One);
			T createCheckedOneTerm = oneTerm * (T.CreateChecked(2) * localValue5 - T.One);
			T localValue6 = y[i] - (localValue3 + localValue4 * localValue5);
			zero += oneTerm * oneTerm;
			zero2 += oneTerm * createCheckedOneTerm;
			zero3 += createCheckedOneTerm * createCheckedOneTerm;
			zero4 += oneTerm * localValue6;
			zero5 += createCheckedOneTerm * localValue6;
			previousX = x[i];
		}
		ValidateLastX(x[x.Length - 1], previousX);
		T zeroZero3Term = zero * zero3 - zero2 * zero2;
		T y2 = zero * zero3;
		T oneY2Term = RoundoffScale<T>() * T.Max(T.One, y2);
		if (lengthOffset < 2 || !T.IsFinite(zeroZero3Term) || zeroZero3Term <= oneY2Term)
		{
			T zeroIds = (IsUsableDenominator(zero) ? (zero4 / zero) : T.Zero);
			fit = new EndpointPolynomialFit<T>(zeroIds, T.Zero);
			if (T.IsFinite(zeroIds))
			{
				return IsMonotonic(localValue4, fit);
			}
			return false;
		}
		T zero4Zero3Term = (zero4 * zero3 - zero5 * zero2) / zeroZero3Term;
		T zero5ZeroTerm = (zero5 * zero - zero4 * zero2) / zeroZero3Term;
		fit = new EndpointPolynomialFit<T>(zero4Zero3Term, zero5ZeroTerm);
		if (T.IsFinite(zero4Zero3Term) && T.IsFinite(zero5ZeroTerm))
		{
			return IsMonotonic(localValue4, fit);
		}
		return false;
	}

	public static bool IsMonotonic<T>(T dy, EndpointPolynomialFit<T> fit) where T : IFloatingPointIeee754<T>
	{
		if (!T.IsFinite(dy) || !T.IsFinite(fit.Bulge) || !T.IsFinite(fit.Twist))
		{
			return false;
		}
		T oneDyTerm = T.Max(T.One, T.Max(T.Abs(dy), T.Max(T.Abs(fit.Bulge), T.Abs(fit.Twist))));
		T localValue = RoundoffScale<T>() * oneDyTerm;
		if (dy == T.Zero)
		{
			if (T.Abs(fit.Bulge) <= localValue)
			{
				return T.Abs(fit.Twist) <= localValue;
			}
			return false;
		}
		T one = ((dy > T.Zero) ? T.One : (-T.One));
		T oneCreateCheckedTerm = one * T.CreateChecked(6) * fit.Twist;
		T oneCreateCheckedTerm2 = one * (T.CreateChecked(2) * fit.Bulge - T.CreateChecked(6) * fit.Twist);
		T oneDyTerm2 = one * (dy - fit.Bulge + fit.Twist);
		T localValue2 = T.Min(oneDyTerm2, oneCreateCheckedTerm + oneCreateCheckedTerm2 + oneDyTerm2);
		if (oneCreateCheckedTerm > T.Zero)
		{
			T createCheckedTerm = -oneCreateCheckedTerm2 / (T.CreateChecked(2) * oneCreateCheckedTerm);
			if (createCheckedTerm > T.Zero && createCheckedTerm < T.One)
			{
				localValue2 = T.Min(localValue2, (oneCreateCheckedTerm * createCheckedTerm + oneCreateCheckedTerm2) * createCheckedTerm + oneDyTerm2);
			}
		}
		return localValue2 >= -localValue;
	}

	private static void ValidateEndpoints<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y) where T : IFloatingPointIeee754<T>
	{
		if (x.Length != y.Length)
		{
			throw new ArgumentException("The x and y spans must have equal lengths.");
		}
		if (x.Length < 2)
		{
			throw new ArgumentException("A fitted segment requires at least two points.", "x");
		}
		if (T.IsFinite(x[0]))
		{
			if (T.IsFinite(x[x.Length - 1]))
			{
				if (T.IsFinite(y[0]))
				{
					if (T.IsFinite(y[y.Length - 1]))
					{
						if (x[x.Length - 1] <= x[0])
						{
							throw new ArgumentException("The x values must be strictly increasing.", "x");
						}
						return;
					}
				}
				throw new ArgumentException("All y values must be finite.", "y");
			}
		}
		throw new ArgumentException("All x values must be finite.", "x");
	}

	private static void ValidateInteriorPoint<T>(T x, T y, T previousX) where T : IFloatingPointIeee754<T>
	{
		if (!T.IsFinite(x) || x <= previousX)
		{
			throw new ArgumentException("The x values must be finite and strictly increasing.", "x");
		}
		if (!T.IsFinite(y))
		{
			throw new ArgumentException("All y values must be finite.", "y");
		}
	}

	private static void ValidateLastX<T>(T x, T previousX) where T : IFloatingPointIeee754<T>
	{
		if (x <= previousX)
		{
			throw new ArgumentException("The x values must be strictly increasing.", "x");
		}
	}

	private static bool IsUsableDenominator<T>(T localValue) where T : IFloatingPointIeee754<T>
	{
		if (T.IsFinite(localValue))
		{
			return localValue > RoundoffScale<T>();
		}
		return false;
	}

	private static T RoundoffScale<T>() where T : IFloatingPointIeee754<T>
	{
		return T.CreateChecked(64) * (T.BitIncrement(T.One) - T.One);
	}
}
