using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using TiffBinaryReader.TagTypeEnums;
using Utility.Logging;

namespace TiffBinaryReader;

public class LZWCodec
{
	private struct code_t
	{
		public int next;

		public short length;

		public byte value;

		public byte firstchar;
	}

	private enum PredictorType
	{
		ptNone,
		ptHorAcc8,
		ptHorAcc16,
		ptHorAcc32,
		ptSwabHorAcc16,
		ptSwabHorAcc32,
		ptHorDiff8,
		ptHorDiff16,
		ptHorDiff32,
		ptFpAcc,
		ptFpDiff
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("LZWCodec");

	private const short BITS_MIN = 9;

	private const short BITS_MAX = 12;

	private const short CODE_CLEAR = 256;

	private const short CODE_EOI = 257;

	private const short CODE_FIRST = 258;

	private const short CODE_MAX = 4095;

	private const short CODE_MIN = 511;

	private const int HSIZE = 9001;

	private const int HSHIFT = 5;

	private const int CSIZE = 5119;

	private const int CHECK_GAP = 10000;

	private bool m_compatDecode;

	private short m_nbits;

	private int m_nextdata;

	private int m_nextbits;

	private int m_dec_nbitsmask;

	private int m_dec_restart;

	private int m_dec_codep;

	private int m_dec_oldcodep;

	private int m_dec_free_entp;

	private int m_dec_maxcodep;

	private code_t[] m_dec_codetab;

	private const short Codec = 66;

	public const int FIELD_PREDICTOR = 66;

	private int m_stride;

	private PredictorType m_predictorType;

	public Predictor Predictor { get; set; }

	public int RowSize { get; set; }

	public int BitsPerSample { get; set; }

	public SampleFormats SampleFormat { get; set; }

	public PlanarConfigs PlanarConfig { get; set; }

	public int SamplesPerPixel { get; set; }

	public static OperationResult CanDecode(TagMetadata meta)
	{
		OperationResult operationResult = true;
		if (meta.CompressionType != CompressionTypes.LZW)
		{
			operationResult.AddFailure("Compression isn't LZW");
		}
		if (meta.SampleFormat != SampleFormats.IEEEFP)
		{
			operationResult.AddFailure("SampleFormat isn't IEEEFP");
		}
		if (meta.BitsPerSample != 32)
		{
			operationResult.AddFailure("Data isn't FP32");
		}
		if (meta.SamplesPerPixel != 1)
		{
			operationResult.AddFailure("Data has multiple channels (must be single band)");
		}
		switch ((Predictor)(meta.Tags.FirstOrDefault((Tag t) => t.TagType == TagTypes.Predictor)?.ValueAs<int>() ?? 0))
		{
		case Predictor.FLOATINGPOINT:
			operationResult.AddFailure("LZW Predictors not fully supported");
			break;
		default:
			operationResult.AddFailure("LZW Predictors not fully supported");
			break;
		case Predictor.NONE:
			break;
		}
		return operationResult;
	}

	public bool Init(TagMetadata meta)
	{
		m_dec_codetab = null;
		m_compatDecode = false;
		int tileWidth = meta.TileWidth;
		int predictor = meta.Tags.FirstOrDefault((Tag t) => t.TagType == TagTypes.Predictor)?.ValueAs<int>() ?? 0;
		Predictor = (Predictor)predictor;
		RowSize = tileWidth * tileWidth * 4;
		SampleFormat = meta.SampleFormat;
		BitsPerSample = meta.BitsPerSample;
		SamplesPerPixel = meta.SamplesPerPixel;
		if (Predictor == Predictor.NONE)
		{
			m_predictorType = PredictorType.ptNone;
		}
		else
		{
			if (Predictor != Predictor.FLOATINGPOINT)
			{
				throw new NotImplementedException();
			}
			m_predictorType = PredictorType.ptFpAcc;
		}
		if (m_predictorType != PredictorType.ptNone)
		{
			PredictorSetupDecode();
		}
		else
		{
			SetupDecode();
		}
		return true;
	}

	private bool SetupDecode()
	{
		if (m_dec_codetab == null)
		{
			m_dec_codetab = new code_t[5119];
			int firstchar = 255;
			do
			{
				m_dec_codetab[firstchar].value = (byte)firstchar;
				m_dec_codetab[firstchar].firstchar = (byte)firstchar;
				m_dec_codetab[firstchar].length = 1;
				m_dec_codetab[firstchar].next = -1;
			}
			while (firstchar-- != 0);
			Array.Clear(m_dec_codetab, 256, 2);
		}
		return true;
	}

	private bool PreDecode(Span<byte> compressedBuf)
	{
		if (m_dec_codetab == null)
		{
			throw new Exception("Should have called Init or similar?");
		}
		if (compressedBuf[0] == 0 && (compressedBuf[1] & 1) != 0)
		{
			throw new Exception("Old-style LZW codes, cannot read file.");
		}
		m_nbits = 9;
		m_nextbits = 0;
		m_nextdata = 0;
		m_dec_restart = 0;
		m_dec_nbitsmask = 511;
		m_dec_free_entp = 258;
		Array.Clear(m_dec_codetab, m_dec_free_entp, 4861);
		m_dec_oldcodep = -1;
		m_dec_maxcodep = m_dec_nbitsmask - 1;
		return true;
	}

	public bool DecodeTile(Span<byte> compressedData, Span<byte> outputBuffer)
	{
		PreDecode(compressedData);
		if (m_predictorType != PredictorType.ptNone)
		{
			return PredictorDecodeTile(compressedData, outputBuffer);
		}
		return DecodeTileInternal(compressedData, outputBuffer);
	}

	private bool DecodeTileInternal(Span<byte> compressedData, Span<byte> outputBuffer)
	{
		return LZWDecodeImpl(compressedData, outputBuffer, m_compatDecode);
	}

	private bool LZWDecodeImpl(Span<byte> compressedData, Span<byte> outputBuffer, bool compat)
	{
		Span<byte> compressedData2 = compressedData;
		int outputBufferLength = outputBuffer.Length;
		int lengthSum = 0;
		_ = m_dec_restart;
		short code = 0;
		while (outputBufferLength > 0)
		{
			compressedData2 = compressedData2.Slice(NextCode(compressedData2, out code, compat));
			if (code == 257)
			{
				break;
			}
			if (code == 256)
			{
				do
				{
					m_dec_free_entp = 258;
					Array.Clear(m_dec_codetab, m_dec_free_entp, 4861);
					m_nbits = 9;
					m_dec_nbitsmask = 511;
					m_dec_maxcodep = m_dec_nbitsmask - ((!compat) ? 1 : 0);
					compressedData2 = compressedData2.Slice(NextCode(compressedData2, out code, compat));
				}
				while (code == 256);
				if (code == 257)
				{
					break;
				}
				if (code > 256)
				{
					throw new Exception("Corrupted LZW table at scanline.");
				}
				outputBuffer[lengthSum] = (byte)code;
				lengthSum++;
				outputBufferLength--;
				m_dec_oldcodep = code;
				continue;
			}
			int code2 = code;
			if (m_dec_free_entp < 0 || m_dec_free_entp >= 5119)
			{
				throw new Exception("Corrupted LZW table at scanline");
			}
			m_dec_codetab[m_dec_free_entp].next = m_dec_oldcodep;
			if (m_dec_codetab[m_dec_free_entp].next < 0 || m_dec_codetab[m_dec_free_entp].next >= 5119)
			{
				throw new Exception("Corrupted LZW table at scanline");
			}
			m_dec_codetab[m_dec_free_entp].firstchar = m_dec_codetab[m_dec_codetab[m_dec_free_entp].next].firstchar;
			m_dec_codetab[m_dec_free_entp].length = (short)(m_dec_codetab[m_dec_codetab[m_dec_free_entp].next].length + 1);
			m_dec_codetab[m_dec_free_entp].value = ((code2 < m_dec_free_entp) ? m_dec_codetab[code2].firstchar : m_dec_codetab[m_dec_free_entp].firstchar);
			if (++m_dec_free_entp > m_dec_maxcodep)
			{
				if (++m_nbits > 12)
				{
					m_nbits = 12;
				}
				m_dec_nbitsmask = MAXCODE(m_nbits);
				m_dec_maxcodep = m_dec_nbitsmask - ((!compat) ? 1 : 0);
			}
			m_dec_oldcodep = code;
			if (code >= 256)
			{
				int lengthSum2 = lengthSum;
				if (m_dec_codetab[code2].length == 0)
				{
					throw new Exception("Wrong length of decoded string: data probably corrupted at scanline");
				}
				if (compat)
				{
					if (m_dec_codetab[code2].length > outputBufferLength)
					{
						m_dec_codep = code;
						do
						{
							code2 = m_dec_codetab[code2].next;
						}
						while (m_dec_codetab[code2].length > outputBufferLength);
						m_dec_restart = outputBufferLength;
						int outputBufferLength2 = outputBufferLength;
						do
						{
							outputBufferLength2--;
							outputBuffer[lengthSum + outputBufferLength2] = m_dec_codetab[code2].value;
							code2 = m_dec_codetab[code2].next;
						}
						while (--outputBufferLength != 0);
						break;
					}
					lengthSum += m_dec_codetab[code2].length;
					outputBufferLength -= m_dec_codetab[code2].length;
					int lengthSum3 = lengthSum;
					do
					{
						lengthSum3--;
						outputBuffer[lengthSum3] = m_dec_codetab[code2].value;
						code2 = m_dec_codetab[code2].next;
					}
					while (code2 != -1 && lengthSum3 > lengthSum2);
					continue;
				}
				if (m_dec_codetab[code2].length > outputBufferLength)
				{
					m_dec_codep = code;
					do
					{
						code2 = m_dec_codetab[code2].next;
					}
					while (code2 != -1 && m_dec_codetab[code2].length > outputBufferLength);
					if (code2 == -1)
					{
						break;
					}
					m_dec_restart = outputBufferLength;
					int outputBufferLength3 = outputBufferLength;
					do
					{
						outputBufferLength3--;
						outputBuffer[lengthSum + outputBufferLength3] = m_dec_codetab[code2].value;
						code2 = m_dec_codetab[code2].next;
					}
					while (--outputBufferLength != 0 && code2 != -1);
					if (code2 == -1)
					{
						break;
					}
					throw new Exception("LZWDecode: Bogus encoding, loop in the code table.");
				}
				int length = m_dec_codetab[code2].length;
				int length2 = length;
				do
				{
					length2--;
					int outputBuffer2 = m_dec_codetab[code2].value;
					code2 = m_dec_codetab[code2].next;
					outputBuffer[lengthSum + length2] = (byte)outputBuffer2;
				}
				while (code2 != -1 && length2 > 0);
				if (code2 != -1)
				{
					throw new Exception("LZWDecode: Bogus encoding, loop in the code table; scanline.");
				}
				lengthSum += length;
				outputBufferLength -= length;
			}
			else
			{
				outputBuffer[lengthSum] = (byte)code;
				lengthSum++;
				outputBufferLength--;
			}
		}
		if (outputBufferLength > 0)
		{
			throw new Exception($"Not enough data at scanline. (short {outputBufferLength} bytes)");
		}
		return true;
	}

	private static int MAXCODE(int n)
	{
		return (1 << n) - 1;
	}

	private int NextCode(Span<byte> compressedData, out short code, bool compat)
	{
		if (compat)
		{
			return GetNextCodeCompat(compressedData, out code);
		}
		return GetNextCode(compressedData, out code);
	}

	private int GetNextCode(Span<byte> data, out short code)
	{
		m_nextdata = (m_nextdata << 8) | data[0];
		int nextCode = 1;
		m_nextbits += 8;
		if (m_nextbits < m_nbits)
		{
			m_nextdata = (m_nextdata << 8) | data[nextCode];
			nextCode++;
			m_nextbits += 8;
		}
		code = (short)((m_nextdata >> m_nextbits - m_nbits) & m_dec_nbitsmask);
		m_nextbits -= m_nbits;
		return nextCode;
	}

	private int GetNextCodeCompat(Span<byte> data, out short code)
	{
		m_nextdata |= data[0] << m_nextbits;
		int nextCodeCompat = 1;
		m_nextbits += 8;
		if (m_nextbits < m_nbits)
		{
			m_nextdata |= data[nextCodeCompat] << m_nextbits;
			nextCodeCompat++;
			m_nextbits += 8;
		}
		code = (short)(m_nextdata & m_dec_nbitsmask);
		m_nextdata >>= (int)m_nbits;
		m_nextbits -= m_nbits;
		return nextCodeCompat;
	}

	private void predictorFunc(Span<byte> buffer)
	{
		switch (m_predictorType)
		{
		case PredictorType.ptHorAcc8:
			horAcc8(buffer);
			break;
		case PredictorType.ptHorAcc16:
			horAcc16(buffer);
			break;
		case PredictorType.ptHorAcc32:
			horAcc32(buffer);
			break;
		case PredictorType.ptSwabHorAcc16:
			swabHorAcc16(buffer);
			break;
		case PredictorType.ptSwabHorAcc32:
			swabHorAcc32(buffer);
			break;
		case PredictorType.ptHorDiff8:
			horDiff8(buffer);
			break;
		case PredictorType.ptHorDiff16:
			horDiff16(buffer);
			break;
		case PredictorType.ptHorDiff32:
			horDiff32(buffer);
			break;
		case PredictorType.ptFpAcc:
			fpAcc(buffer);
			break;
		case PredictorType.ptFpDiff:
			fpDiff(buffer);
			break;
		}
	}

	private void horAcc8(Span<byte> buffer)
	{
		int ordinal = 0;
		int length = buffer.Length;
		if (length <= m_stride)
		{
			return;
		}
		length -= m_stride;
		if (m_stride == 3)
		{
			int ordinal2 = buffer[ordinal];
			int ordinal3 = buffer[ordinal + 1];
			int ordinal4 = buffer[ordinal + 2];
			do
			{
				length -= 3;
				ordinal += 3;
				ordinal2 += buffer[ordinal];
				buffer[ordinal] = (byte)ordinal2;
				ordinal3 += buffer[ordinal + 1];
				buffer[ordinal + 1] = (byte)ordinal3;
				ordinal4 += buffer[ordinal + 2];
				buffer[ordinal + 2] = (byte)ordinal4;
			}
			while (length > 0);
			return;
		}
		if (m_stride == 4)
		{
			int ordinal5 = buffer[ordinal];
			int ordinal6 = buffer[ordinal + 1];
			int ordinal7 = buffer[ordinal + 2];
			int ordinal8 = buffer[ordinal + 3];
			do
			{
				length -= 4;
				ordinal += 4;
				ordinal5 += buffer[ordinal];
				buffer[ordinal] = (byte)ordinal5;
				ordinal6 += buffer[ordinal + 1];
				buffer[ordinal + 1] = (byte)ordinal6;
				ordinal7 += buffer[ordinal + 2];
				buffer[ordinal + 2] = (byte)ordinal7;
				ordinal8 += buffer[ordinal + 3];
				buffer[ordinal + 3] = (byte)ordinal8;
			}
			while (length > 0);
			return;
		}
		do
		{
			for (int m_stride2 = m_stride; m_stride2 > 0; m_stride2--)
			{
				buffer[ordinal + m_stride] = (byte)(buffer[ordinal + m_stride] + buffer[ordinal]);
				ordinal++;
			}
			length -= m_stride;
		}
		while (length > 0);
	}

	private short[] ByteArrayToShorts(Span<byte> buffer)
	{
		throw new NotImplementedException();
	}

	private int[] ByteArrayToInts(Span<byte> buffer)
	{
		throw new NotImplementedException();
	}

	public static void IntsToByteArray(Span<int> source, Span<byte> output)
	{
		throw new NotImplementedException();
	}

	public static void ShortsToByteArray(Span<short> source, Span<byte> dest)
	{
		throw new NotImplementedException();
	}

	public static void SwabArrayOfLong(int[] intValues, int count)
	{
		SwabArrayOfLong(intValues, 0, count);
	}

	public static void SwabArrayOfLong(int[] intValues, int offset, int count)
	{
		throw new NotImplementedException();
	}

	public static void SwabArrayOfShort(short[] shortValues, int count)
	{
		SwabArrayOfShort(shortValues, 0, count);
	}

	public static void SwabArrayOfShort(short[] shortValues, int offset, int count)
	{
		throw new NotImplementedException();
	}

	private void horAcc16(Span<byte> buffer)
	{
		int length = buffer.Length;
		short[] byteArrayToShorts = ByteArrayToShorts(buffer);
		int ordinal = 0;
		int m_strideSum = length / 2;
		if (m_strideSum > m_stride)
		{
			m_strideSum -= m_stride;
			do
			{
				for (int m_stride2 = m_stride; m_stride2 > 0; m_stride2--)
				{
					byteArrayToShorts[ordinal + m_stride] += byteArrayToShorts[ordinal];
					ordinal++;
				}
				m_strideSum -= m_stride;
			}
			while (m_strideSum > 0);
		}
		ShortsToByteArray(byteArrayToShorts, buffer);
	}

	private void horAcc32(Span<byte> buffer)
	{
		int length = buffer.Length;
		int[] byteArrayToInts = ByteArrayToInts(buffer);
		int ordinal = 0;
		int m_strideSum = length / 4;
		if (m_strideSum > m_stride)
		{
			m_strideSum -= m_stride;
			do
			{
				for (int m_stride2 = m_stride; m_stride2 > 0; m_stride2--)
				{
					byteArrayToInts[ordinal + m_stride] += byteArrayToInts[ordinal];
					ordinal++;
				}
				m_strideSum -= m_stride;
			}
			while (m_strideSum > 0);
		}
		IntsToByteArray(byteArrayToInts, buffer);
	}

	private void swabHorAcc16(Span<byte> buffer)
	{
		int length = buffer.Length;
		short[] byteArrayToShorts = ByteArrayToShorts(buffer);
		int ordinal = 0;
		int m_strideSum = length / 2;
		if (m_strideSum > m_stride)
		{
			SwabArrayOfShort(byteArrayToShorts, m_strideSum);
			m_strideSum -= m_stride;
			do
			{
				for (int m_stride2 = m_stride; m_stride2 > 0; m_stride2--)
				{
					byteArrayToShorts[ordinal + m_stride] += byteArrayToShorts[ordinal];
					ordinal++;
				}
				m_strideSum -= m_stride;
			}
			while (m_strideSum > 0);
		}
		ShortsToByteArray(byteArrayToShorts, buffer);
	}

	private void swabHorAcc32(Span<byte> buffer)
	{
		int length = buffer.Length;
		int[] byteArrayToInts = ByteArrayToInts(buffer);
		int ordinal = 0;
		int m_strideSum = length / 4;
		if (m_strideSum > m_stride)
		{
			SwabArrayOfLong(byteArrayToInts, m_strideSum);
			m_strideSum -= m_stride;
			do
			{
				for (int m_stride2 = m_stride; m_stride2 > 0; m_stride2--)
				{
					byteArrayToInts[ordinal + m_stride] += byteArrayToInts[ordinal];
					ordinal++;
				}
				m_strideSum -= m_stride;
			}
			while (m_strideSum > 0);
		}
		IntsToByteArray(byteArrayToInts, buffer);
	}

	private void horDiff8(Span<byte> buffer)
	{
		int ordinal = 0;
		int length = buffer.Length;
		if (length <= m_stride)
		{
			return;
		}
		length -= m_stride;
		int ordinal2 = ordinal;
		if (m_stride == 3)
		{
			int ordinal3 = buffer[ordinal2];
			int ordinal4 = buffer[ordinal2 + 1];
			int ordinal5 = buffer[ordinal2 + 2];
			do
			{
				int ordinal6 = buffer[ordinal2 + 3];
				buffer[ordinal2 + 3] = (byte)(ordinal6 - ordinal3);
				ordinal3 = ordinal6;
				int ordinal7 = buffer[ordinal2 + 4];
				buffer[ordinal2 + 4] = (byte)(ordinal7 - ordinal4);
				ordinal4 = ordinal7;
				int ordinal8 = buffer[ordinal2 + 5];
				buffer[ordinal2 + 5] = (byte)(ordinal8 - ordinal5);
				ordinal5 = ordinal8;
				ordinal2 += 3;
			}
			while ((length -= 3) > 0);
			return;
		}
		if (m_stride == 4)
		{
			int ordinal9 = buffer[ordinal2];
			int ordinal10 = buffer[ordinal2 + 1];
			int ordinal11 = buffer[ordinal2 + 2];
			int ordinal12 = buffer[ordinal2 + 3];
			do
			{
				int ordinal13 = buffer[ordinal2 + 4];
				buffer[ordinal2 + 4] = (byte)(ordinal13 - ordinal9);
				ordinal9 = ordinal13;
				int ordinal14 = buffer[ordinal2 + 5];
				buffer[ordinal2 + 5] = (byte)(ordinal14 - ordinal10);
				ordinal10 = ordinal14;
				int ordinal15 = buffer[ordinal2 + 6];
				buffer[ordinal2 + 6] = (byte)(ordinal15 - ordinal11);
				ordinal11 = ordinal15;
				int ordinal16 = buffer[ordinal2 + 7];
				buffer[ordinal2 + 7] = (byte)(ordinal16 - ordinal12);
				ordinal12 = ordinal16;
				ordinal2 += 4;
			}
			while ((length -= 4) > 0);
			return;
		}
		ordinal2 += length - 1;
		do
		{
			for (int m_stride2 = m_stride; m_stride2 > 0; m_stride2--)
			{
				buffer[ordinal2 + m_stride] -= buffer[ordinal2];
				ordinal2--;
			}
		}
		while ((length -= m_stride) > 0);
	}

	private void horDiff16(Span<byte> buffer)
	{
		int length = buffer.Length;
		short[] byteArrayToShorts = ByteArrayToShorts(buffer);
		int m_strideSumOffsetSum = 0;
		int m_strideSum = length / 2;
		if (m_strideSum > m_stride)
		{
			m_strideSum -= m_stride;
			m_strideSumOffsetSum += m_strideSum - 1;
			do
			{
				for (int m_stride2 = m_stride; m_stride2 > 0; m_stride2--)
				{
					byteArrayToShorts[m_strideSumOffsetSum + m_stride] -= byteArrayToShorts[m_strideSumOffsetSum];
					m_strideSumOffsetSum--;
				}
				m_strideSum -= m_stride;
			}
			while (m_strideSum > 0);
		}
		ShortsToByteArray(byteArrayToShorts, buffer);
	}

	private void horDiff32(Span<byte> buffer)
	{
		int length = buffer.Length;
		int[] byteArrayToInts = ByteArrayToInts(buffer);
		int m_strideSumOffsetSum = 0;
		int m_strideSum = length / 4;
		if (m_strideSum > m_stride)
		{
			m_strideSum -= m_stride;
			m_strideSumOffsetSum += m_strideSum - 1;
			do
			{
				for (int m_stride2 = m_stride; m_stride2 > 0; m_stride2--)
				{
					byteArrayToInts[m_strideSumOffsetSum + m_stride] -= byteArrayToInts[m_strideSumOffsetSum];
					m_strideSumOffsetSum--;
				}
				m_strideSum -= m_stride;
			}
			while (m_strideSum > 0);
		}
		IntsToByteArray(byteArrayToInts, buffer);
	}

	private void fpAcc(Span<byte> buffer)
	{
		int length = buffer.Length;
		int ordinal = 0;
		int count = BitsPerSample / 8;
		int count2 = length / count;
		int length2 = length;
		int ordinal2 = ordinal;
		while (length2 > m_stride)
		{
			for (int m_stride2 = m_stride; m_stride2 > 0; m_stride2--)
			{
				buffer[ordinal2 + m_stride] += buffer[ordinal2];
				ordinal2++;
			}
			length2 -= m_stride;
		}
		byte[] byteValues = new byte[length];
		buffer.CopyTo(byteValues);
		for (int i = 0; i < count2; i++)
		{
			for (int j = 0; j < count; j++)
			{
				buffer[ordinal + count * i + j] = byteValues[(count - j - 1) * count2 + i];
			}
		}
	}

	private void fpDiff(Span<byte> buffer)
	{
		int length = buffer.Length;
		int ordinal = 0;
		byte[] byteValues = new byte[length];
		buffer.CopyTo(byteValues);
		int count = BitsPerSample / 8;
		int count2 = length / count;
		for (int i = 0; i < count2; i++)
		{
			for (int j = 0; j < count; j++)
			{
				buffer[ordinal + (count - j - 1) * count2 + i] = byteValues[count * i + j];
			}
		}
		int lengthM_strideTerm = ordinal + length - m_stride - 1;
		for (int length2 = length; length2 > m_stride; length2 -= m_stride)
		{
			for (int m_stride2 = m_stride; m_stride2 > 0; m_stride2--)
			{
				buffer[lengthM_strideTerm + m_stride] -= buffer[lengthM_strideTerm];
				lengthM_strideTerm--;
			}
		}
	}

	private bool PredictorDecodeTile(Span<byte> compressedData, Span<byte> outputBuffer)
	{
		if (DecodeTileInternal(compressedData, outputBuffer))
		{
			int outputBufferLength = outputBuffer.Length;
			int rowSizeSum = 0;
			while (outputBufferLength > 0)
			{
				predictorFunc(outputBuffer.Slice(rowSizeSum, RowSize));
				outputBufferLength -= RowSize;
				rowSizeSum += RowSize;
			}
			return true;
		}
		return false;
	}

	private bool PredictorSetupDecode()
	{
		if (!SetupDecode() || !PredictorSetup())
		{
			return false;
		}
		if (Predictor == Predictor.HORIZONTAL)
		{
			switch (BitsPerSample)
			{
			case 8:
				m_predictorType = PredictorType.ptHorAcc8;
				break;
			case 16:
				m_predictorType = PredictorType.ptHorAcc16;
				break;
			case 32:
				m_predictorType = PredictorType.ptHorAcc32;
				break;
			}
		}
		else if (Predictor == Predictor.FLOATINGPOINT)
		{
			m_predictorType = PredictorType.ptFpAcc;
		}
		return true;
	}

	private bool PredictorSetup()
	{
		switch (Predictor)
		{
		case Predictor.NONE:
			return true;
		case Predictor.HORIZONTAL:
			if (BitsPerSample != 8 && BitsPerSample != 16 && BitsPerSample != 32)
			{
				throw new Exception($"Horizontal differencing \"Predictor\" not supported with {BitsPerSample}-bit samples");
			}
			break;
		case Predictor.FLOATINGPOINT:
			if (SampleFormat != SampleFormats.IEEEFP)
			{
				throw new Exception($"Floating point \"Predictor\" not supported with {SampleFormat} data format");
			}
			break;
		default:
			throw new Exception($"\"Predictor\" not supported {Predictor}");
		}
		m_stride = ((PlanarConfig != PlanarConfigs.Contiguous) ? 1 : SamplesPerPixel);
		RowSize = -1;
		return true;
	}
}
