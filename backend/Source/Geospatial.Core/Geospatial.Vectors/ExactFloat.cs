using System;
using System.Numerics;

namespace Geospatial.Vectors;

public struct ExactFloat
{
	public readonly BigInteger S;

	public readonly BigInteger E;

	public ExactFloat(BigInteger S, BigInteger E)
	{
		this.S = S;
		this.E = E;
	}

	public static ExactFloat operator +(ExactFloat a, ExactFloat b)
	{
		if (a.E < b.E)
		{
			return b + a;
		}
		new BigInteger(1);
		int ordinal = (int)(a.E - b.E);
		return new ExactFloat((a.S << ordinal) + b.S, b.E);
	}

	public static ExactFloat operator -(ExactFloat a, ExactFloat b)
	{
		return a + b.Negate();
	}

	public ExactFloat Negate()
	{
		return new ExactFloat(-S, E);
	}

	public static ExactFloat operator *(ExactFloat a, ExactFloat b)
	{
		return new ExactFloat(a.S * b.S, a.E + b.E);
	}

	public ExactFloat(double doubleValue)
	{
		if (doubleValue == 0.0)
		{
			S = new BigInteger(0);
			E = new BigInteger(0);
			return;
		}
		long doubleToInt64Bits = BitConverter.DoubleToInt64Bits(doubleValue);
		ulong ordinal = 9223372036854775808uL;
		long ordinal2 = 9218868437227405312L;
		long ordinal3 = 4503599627370495L;
		long doubleToInt64BitsTerm = (long)((ulong)doubleToInt64Bits & ordinal) >> 63;
		long doubleToInt64BitsTerm2 = (doubleToInt64Bits & ordinal2) >> 52;
		long doubleToInt64BitsTerm3 = (doubleToInt64Bits & ordinal3) | 0x10000000000000L;
		if (doubleToInt64BitsTerm != 0L)
		{
			doubleToInt64BitsTerm3 *= -1;
		}
		doubleToInt64BitsTerm2 -= 1075;
		S = new BigInteger(doubleToInt64BitsTerm3);
		E = new BigInteger(doubleToInt64BitsTerm2);
	}

	public double AsDouble()
	{
		return (double)S * Math.Pow(2.0, (double)E);
	}
}
