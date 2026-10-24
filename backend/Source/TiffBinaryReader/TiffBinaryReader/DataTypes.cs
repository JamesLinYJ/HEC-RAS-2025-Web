using System;
using System.Linq;

namespace TiffBinaryReader;

public class DataTypes
{
	public static DataType UByte = new DataType(1, "UByte", typeof(byte), 1);

	public static DataType Ascii = new DataType(2, "Ascii", typeof(byte), 1);

	public static DataType UShort = new DataType(3, "UShort", typeof(ushort), 2);

	public static DataType UInt = new DataType(4, "UInt", typeof(uint), 4);

	public static DataType URational = new DataType(5, "URational", typeof(float), 4);

	public static DataType Byte = new DataType(6, "Byte", typeof(sbyte), 1);

	public static DataType Undef = new DataType(7, "Undef", null, 4);

	public static DataType Short = new DataType(8, "Short", typeof(short), 2);

	public static DataType Int = new DataType(9, "Int", typeof(int), 4);

	public static DataType Rational = new DataType(10, "Rational", typeof(float), 4);

	public static DataType Float = new DataType(11, "Float", typeof(float), 4);

	public static DataType Double = new DataType(12, "Double", typeof(double), 8);

	public static DataType IFD = new DataType(13, "IFD", typeof(uint), 4);

	public static DataType ULong8 = new DataType(16, "ULong8", typeof(ulong), 8);

	public static DataType Long8 = new DataType(17, "Long8", typeof(long), 8);

	public static DataType IFD8 = new DataType(18, "IFD8", typeof(ulong), 8);

	public static DataType[] AvailableTypes = new DataType[16]
	{
		UByte, Ascii, UShort, UInt, URational, Byte, Undef, Short, Int, Rational,
		Float, Double, IFD, ULong8, Long8, IFD8
	};

	public static DataType[] AllIntegralTypes = new DataType[8] { Byte, UByte, Short, UShort, Int, UInt, Long8, ULong8 };

	public static DataType[] AllUnsignedIntegralTypes = new DataType[4] { UByte, UShort, UInt, ULong8 };

	public static DataType[] AllShortTypes = new DataType[2] { Short, UShort };

	public static DataType[] AllFloatingTypes = new DataType[2] { Float, Double };

	public static DataType[] AllRationalTypes = new DataType[2] { Rational, URational };

	public static DataType[] AllAsciiTypes = new DataType[1] { Ascii };

	public static DataType Get(ushort typeID)
	{
		return AvailableTypes.FirstOrDefault((DataType dt) => dt.UID == typeID);
	}

	public static DataType Get<T>() where T : unmanaged
	{
		Type tt = typeof(T);
		return AvailableTypes.FirstOrDefault((DataType dt) => dt.Type == tt);
	}
}
