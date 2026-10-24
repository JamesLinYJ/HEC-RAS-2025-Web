using System;
using System.Collections.Generic;
using System.Numerics;

namespace TiffBinaryReader;

public static class ValueConverter
{
	private static Type SByteType;

	private static Type ByteType;

	private static Type ShortType;

	private static Type UShortType;

	private static Type IntType;

	private static Type UIntType;

	private static Type LongType;

	private static Type ULongType;

	private static List<Type> IntegerTypes;

	private static Dictionary<Type, BigInteger> IntegerMaxValues;

	private static Dictionary<Type, BigInteger> IntegerMinValues;

	private static Type SingleType;

	private static Type DoubleType;

	private static List<Type> FloatingTypes;

	private static List<Type> NumericTypes;

	static ValueConverter()
	{
		SByteType = typeof(sbyte);
		ByteType = typeof(byte);
		ShortType = typeof(short);
		UShortType = typeof(ushort);
		IntType = typeof(int);
		UIntType = typeof(uint);
		LongType = typeof(long);
		ULongType = typeof(ulong);
		IntegerTypes = new List<Type> { SByteType, ByteType, ShortType, UShortType, IntType, UIntType, LongType, ULongType };
		IntegerMaxValues = new Dictionary<Type, BigInteger>
		{
			{
				SByteType,
				new BigInteger(127)
			},
			{
				ByteType,
				new BigInteger(255)
			},
			{
				ShortType,
				new BigInteger(32767)
			},
			{
				UShortType,
				new BigInteger(65535)
			},
			{
				IntType,
				new BigInteger(int.MaxValue)
			},
			{
				UIntType,
				new BigInteger(uint.MaxValue)
			},
			{
				LongType,
				new BigInteger(long.MaxValue)
			},
			{
				ULongType,
				new BigInteger(ulong.MaxValue)
			}
		};
		IntegerMinValues = new Dictionary<Type, BigInteger>
		{
			{
				SByteType,
				new BigInteger(-128)
			},
			{
				ByteType,
				new BigInteger(0)
			},
			{
				ShortType,
				new BigInteger(-32768)
			},
			{
				UShortType,
				new BigInteger(0)
			},
			{
				IntType,
				new BigInteger(int.MinValue)
			},
			{
				UIntType,
				new BigInteger(0u)
			},
			{
				LongType,
				new BigInteger(long.MinValue)
			},
			{
				ULongType,
				new BigInteger(0uL)
			}
		};
		SingleType = typeof(float);
		DoubleType = typeof(double);
		FloatingTypes = new List<Type> { SingleType, DoubleType };
		NumericTypes = new List<Type>();
		NumericTypes.AddRange(IntegerTypes);
		NumericTypes.AddRange(FloatingTypes);
	}

	private static BigInteger AsBigInt(object v)
	{
		Type type = v.GetType();
		if (type == SByteType)
		{
			return new BigInteger((sbyte)v);
		}
		if (type == ByteType)
		{
			return new BigInteger((byte)v);
		}
		if (type == ShortType)
		{
			return new BigInteger((short)v);
		}
		if (type == UShortType)
		{
			return new BigInteger((ushort)v);
		}
		if (type == IntType)
		{
			return new BigInteger((int)v);
		}
		if (type == UIntType)
		{
			return new BigInteger((uint)v);
		}
		if (type == LongType)
		{
			return new BigInteger((long)v);
		}
		if (type == ULongType)
		{
			return new BigInteger((ulong)v);
		}
		throw new InvalidCastException();
	}

	private static T AsT<T>(BigInteger bigInteger)
	{
		Type typeFromHandle = typeof(T);
		if (typeFromHandle == SByteType)
		{
			return (T)(object)(sbyte)bigInteger;
		}
		if (typeFromHandle == ByteType)
		{
			return (T)(object)(byte)bigInteger;
		}
		if (typeFromHandle == ShortType)
		{
			return (T)(object)(short)bigInteger;
		}
		if (typeFromHandle == UShortType)
		{
			return (T)(object)(ushort)bigInteger;
		}
		if (typeFromHandle == IntType)
		{
			return (T)(object)(int)bigInteger;
		}
		if (typeFromHandle == UIntType)
		{
			return (T)(object)(uint)bigInteger;
		}
		if (typeFromHandle == LongType)
		{
			return (T)(object)(long)bigInteger;
		}
		if (typeFromHandle == ULongType)
		{
			return (T)(object)(ulong)bigInteger;
		}
		throw new InvalidCastException();
	}

	public static T To<T>(object v) where T : struct
	{
		Type type = v.GetType();
		Type typeFromHandle = typeof(T);
		if (type == typeFromHandle)
		{
			return (T)v;
		}
		if (!NumericTypes.Contains(typeFromHandle))
		{
			throw new InvalidCastException();
		}
		if (!NumericTypes.Contains(type))
		{
			throw new InvalidCastException();
		}
		if (IntegerTypes.Contains(typeFromHandle) && IntegerTypes.Contains(type))
		{
			BigInteger bigInteger = IntegerMaxValues[typeFromHandle];
			BigInteger bigInteger2 = IntegerMinValues[typeFromHandle];
			BigInteger bigInteger3 = AsBigInt(v);
			if (bigInteger3 > bigInteger || bigInteger3 < bigInteger2)
			{
				throw new InvalidCastException();
			}
			return AsT<T>(bigInteger3);
		}
		if (FloatingTypes.Contains(typeFromHandle) && FloatingTypes.Contains(type))
		{
			if (typeFromHandle == SingleType && type == DoubleType)
			{
				double computed = (double)v;
				if (computed > 3.4028234663852886E+38 || computed < -3.4028234663852886E+38)
				{
					throw new InvalidCastException();
				}
				return (T)(object)(float)computed;
			}
			if (typeFromHandle == DoubleType && type == SingleType)
			{
				return (T)(object)(double)(float)v;
			}
			throw new InvalidCastException();
		}
		throw new InvalidCastException();
	}
}
