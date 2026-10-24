using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Geospatial.GDALAssist;

namespace Geospatial.IO;

public class GribReader
{
	public enum GribTable5CompressionSpec
	{
		GridPointDataSimplePacking_0,
		MatrixValueAtGridPointSimplePacking_1,
		GridPointDataComplexPacking_2,
		GridPointDataComplexPackingAndSpatialDifferencing_3,
		GridPointDataIEEEFPData_4,
		GridPointDataJPEG2000_40,
		GridPointDataPNG_41,
		GridPointDataCCSDSLossless_42,
		SpectralDataSimplePacking_50,
		SpectralDataComplexPacking_51,
		SpectralDataLimitedAreaComplexPacking_53,
		GridPointDataSimplePackingLogPreprocessing_61,
		RunLengthPackingLevelValues_200,
		Missing_65535
	}

	public static List<GdalRasterSummary> CreateRasterSummaries(string keyFrame, IList<string> files)
	{
		using GdalRasterCollection gdalRasterCollection = GdalRasterCollection.Open(files);
		GDALRaster.FindMatchingFiles(keyFrame, files, out var _, gdalRasterCollection);
		return GdalRasterSummary.ParseCollection(gdalRasterCollection);
	}

	public static void TestGrib(string fn)
	{
		ValidateHeader(File.ReadAllBytes(fn));
	}

	private static bool ValidateHeader(Span<byte> bytes)
	{
		if (!ParseSection0(bytes, out var _))
		{
			return false;
		}
		bytes = bytes.Slice(16);
		PeekSectionHeader(bytes, out var sectionLength, out var sectionNum);
		if (sectionNum != 1)
		{
			return false;
		}
		if (!ParseSection1(bytes, out var _))
		{
			return false;
		}
		bytes = bytes.Slice(sectionLength);
		PeekSectionHeader(bytes, out sectionLength, out sectionNum);
		if (sectionNum == 2)
		{
			ParseSection2(bytes);
			PeekSectionHeader(bytes, out sectionLength, out sectionNum);
			bytes = bytes.Slice(sectionLength);
		}
		if (sectionNum == 3)
		{
			ParseSection3(bytes);
			bytes = bytes.Slice(sectionLength);
			PeekSectionHeader(bytes, out sectionLength, out sectionNum);
		}
		if (sectionNum == 4)
		{
			ParseSection4(bytes);
			bytes = bytes.Slice(sectionLength);
			PeekSectionHeader(bytes, out sectionLength, out sectionNum);
		}
		if (sectionNum == 5)
		{
			ParseSection5(bytes, out var _);
			bytes = bytes.Slice(sectionLength);
			PeekSectionHeader(bytes, out sectionLength, out sectionNum);
		}
		if (sectionNum == 6)
		{
			ParseSection6(bytes);
			bytes = bytes.Slice(sectionLength);
			PeekSectionHeader(bytes, out sectionLength, out sectionNum);
		}
		if (sectionNum == 7)
		{
			ParseSection7(bytes);
			bytes = bytes.Slice(sectionLength);
			PeekSectionHeader(bytes, out sectionLength, out sectionNum);
		}
		_ = 8;
		return true;
	}

	private static bool ParseSection0(Span<byte> bytes, out long expectedFileLen)
	{
		expectedFileLen = 0L;
		if (bytes[0] != 71)
		{
			return false;
		}
		if (bytes[1] != 82)
		{
			return false;
		}
		if (bytes[2] != 73)
		{
			return false;
		}
		if (bytes[3] != 66)
		{
			return false;
		}
		if (bytes[4] != 0)
		{
			return false;
		}
		if (bytes[5] != 0)
		{
			return false;
		}
		_ = ref bytes[6];
		if (bytes[7] != 2)
		{
			return false;
		}
		expectedFileLen = GetLong(bytes.Slice(8));
		return true;
	}

	private static bool ParseSection1(Span<byte> bytes, out DateTime refTime)
	{
		refTime = DateTime.MinValue;
		if (GetInt(bytes) != 21)
		{
			return false;
		}
		GetShort(bytes.Slice(5));
		GetShort(bytes.Slice(7));
		_ = ref bytes[9];
		_ = ref bytes[10];
		_ = ref bytes[11];
		int year = GetShort(bytes.Slice(12));
		byte month = bytes[14];
		byte day = bytes[15];
		byte hour = bytes[16];
		byte minute = bytes[17];
		byte second = bytes[18];
		refTime = new DateTime(year, month, day, hour, minute, second);
		return true;
	}

	private static bool ParseSection2(Span<byte> bytes)
	{
		return true;
	}

	private static bool ParseSection3(Span<byte> bytes)
	{
		_ = ref bytes[5];
		GetInt(bytes.Slice(6));
		_ = ref bytes[10];
		_ = ref bytes[11];
		GetShort(bytes.Slice(12));
		return true;
	}

	private static bool ParseSection4(Span<byte> bytes)
	{
		return true;
	}

	private static bool ParseSection5(Span<byte> bytes, out short table5Value)
	{
		table5Value = GetShort(bytes.Slice(9));
		Span<byte> bytes2 = bytes.Slice(11);
		if (table5Value == 41)
		{
			GetFloat(bytes2);
			GetShort(bytes2.Slice(4));
			GetShort(bytes2.Slice(6));
			_ = ref bytes2[8];
			_ = ref bytes2[9];
			_ = ref bytes2[10];
		}
		return true;
	}

	private static bool ParseSection6(Span<byte> bytes)
	{
		return true;
	}

	private static bool ParseSection7(Span<byte> bytes)
	{
		return true;
	}

	private static void PeekSectionHeader(Span<byte> bytes, out int sectionLength, out int sectionNum)
	{
		sectionLength = GetInt(bytes);
		sectionNum = bytes[4];
	}

	private unsafe static long GetLong(Span<byte> bytes)
	{
		sbyte* bytesTerm = stackalloc sbyte[8]
		{
			(sbyte)bytes[7],
			(sbyte)bytes[6],
			(sbyte)bytes[5],
			(sbyte)bytes[4],
			(sbyte)bytes[3],
			(sbyte)bytes[2],
			(sbyte)bytes[1],
			(sbyte)bytes[0]
		};
		return Unsafe.As<byte, long>(ref *(byte*)bytesTerm);
	}

	private unsafe static int GetInt(Span<byte> bytes)
	{
		sbyte* bytesTerm = stackalloc sbyte[4]
		{
			(sbyte)bytes[3],
			(sbyte)bytes[2],
			(sbyte)bytes[1],
			(sbyte)bytes[0]
		};
		return Unsafe.As<byte, int>(ref *(byte*)bytesTerm);
	}

	private unsafe static float GetFloat(Span<byte> bytes)
	{
		sbyte* bytesTerm = stackalloc sbyte[4]
		{
			(sbyte)bytes[3],
			(sbyte)bytes[2],
			(sbyte)bytes[1],
			(sbyte)bytes[0]
		};
		return Unsafe.As<byte, float>(ref *(byte*)bytesTerm);
	}

	private unsafe static short GetShort(Span<byte> bytes)
	{
		byte* localValue = stackalloc byte[4];
		*localValue = bytes[1];
		localValue[1] = bytes[0];
		return Unsafe.As<byte, short>(ref *localValue);
	}
}
