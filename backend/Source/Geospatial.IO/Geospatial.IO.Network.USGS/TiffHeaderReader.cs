using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Web;

namespace Geospatial.IO.Network.USGS;

public static class TiffHeaderReader
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TiffHeaderReader");

	private static readonly HttpClient _client = SharedWebResourceManager.Client;

	private const int SmallHeaderSize = 4096;

	private const int LargeHeaderSize = 65536;

	private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15L);

	private const int TagImageWidth = 256;

	private const int TagImageLength = 257;

	private const int TagModelPixelScale = 33550;

	private const int TagModelTiepoint = 33922;

	private const int TagModelTransformation = 34264;

	private const int TagGeoKeyDirectory = 34735;

	private const int TagGeoDoubleParams = 34736;

	private const int TagGeoAsciiParams = 34737;

	private const int GeoKeyProjectedCSType = 3072;

	private const int GeoKeyGeographicType = 2048;

	public static async Task<TileBoundsResult> ReadBoundsAsync(string url, CancellationToken ct = default(CancellationToken))
	{
		return await ReadBoundsAndSizeAsync(url, ct);
	}

	public static async Task<TileBoundsResult> ReadBoundsAndSizeAsync(string url, CancellationToken ct = default(CancellationToken))
	{
		_ = 1;
		try
		{
			(Extent, long, bool, bool) tuple = await FetchAndParseAsync(url, 4096, ct);
			if (tuple.Item3)
			{
				return new TileBoundsResult(tuple.Item1, tuple.Item2, success: true);
			}
			if (!tuple.Item4)
			{
				return TileBoundsResult.FailedWithSize(tuple.Item2);
			}
			tuple = await FetchAndParseAsync(url, 65536, ct);
			if (tuple.Item3)
			{
				return new TileBoundsResult(tuple.Item1, tuple.Item2, success: true);
			}
			return TileBoundsResult.FailedWithSize(tuple.Item2);
		}
		catch (OperationCanceledException)
		{
			return TileBoundsResult.Failed;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error reading TIFF header from {Url}", url);
			return TileBoundsResult.Failed;
		}
	}

	private static async Task<(Extent Bounds, long SizeInBytes, bool Success, bool NeedsMoreData)> FetchAndParseAsync(string url, int headerSize, CancellationToken ct)
	{
		byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(headerSize);
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
			request.Headers.Range = new RangeHeaderValue(0L, headerSize - 1);
			using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
			if (!response.IsSuccessStatusCode)
			{
				if (response.StatusCode == HttpStatusCode.NotFound)
				{
					_logger.LogTrace("Tile not found: {Url}", url);
				}
				else
				{
					_logger.LogWarning("Failed to read TIFF header from {Url}: {Status}", url, response.StatusCode);
				}
				return (Bounds: default, SizeInBytes: 0L, Success: false, NeedsMoreData: false);
			}
			long sizeInBytes = 0L;
			if (response.Content.Headers.ContentRange?.Length.HasValue ?? false)
			{
				sizeInBytes = response.Content.Headers.ContentRange.Length.Value;
			}
			using CancellationTokenSource readCts = (ct.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(ct) : new CancellationTokenSource());
			using Stream stream = await response.Content.ReadAsStreamAsync(ct);
			int totalRead;
			int readAsync;
			for (totalRead = 0; totalRead < headerSize; totalRead += readAsync)
			{
				readCts.CancelAfter(ReadTimeout);
				readAsync = await stream.ReadAsync(rentedBuffer.AsMemory(totalRead, headerSize - totalRead), readCts.Token);
				if (readAsync == 0)
				{
					break;
				}
			}
			if (totalRead < 8)
			{
				_logger.LogWarning("TIFF header too small from {Url}", url);
				return (Bounds: default, SizeInBytes: 0L, Success: false, NeedsMoreData: false);
			}
			(Extent, bool, bool) tuple = ParseGeoTiffBounds(rentedBuffer.AsSpan(0, totalRead), url);
			return (Bounds: tuple.Item1, SizeInBytes: sizeInBytes, Success: tuple.Item2, NeedsMoreData: tuple.Item3);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rentedBuffer);
		}
	}

	private static (Extent Bounds, bool Success, bool NeedsMoreData) ParseGeoTiffBounds(ReadOnlySpan<byte> header, string url)
	{
		bool littleEndian = header[0] == 73;
		ushort uInt16 = ReadUInt16(header, 2, littleEndian);
		if (uInt16 != 42)
		{
			_logger.LogTrace("Invalid TIFF magic number: {Magic}", uInt16);
			return (Bounds: default, Success: false, NeedsMoreData: false);
		}
		uint uInt32 = ReadUInt32(header, 4, littleEndian);
		if (uInt32 + 2 >= header.Length)
		{
			return (Bounds: default, Success: false, NeedsMoreData: true);
		}
		ushort uInt162 = ReadUInt16(header, (int)uInt32, littleEndian);
		int uInt322 = 0;
		int uInt323 = 0;
		double[] doubleArray = null;
		double[] doubleArrayArray = null;
		int uInt16Array = 0;
		int uInt16Array2 = 0;
		ushort[] uInt16ArrayArray = null;
		bool condition = false;
		for (int i = 0; i < uInt162; i++)
		{
			int uInt32Term = (int)(uInt32 + 2) + i * 12;
			if (uInt32Term + 12 > header.Length)
			{
				condition = true;
				break;
			}
			ushort uInt163 = ReadUInt16(header, uInt32Term, littleEndian);
			ushort uInt164 = ReadUInt16(header, uInt32Term + 2, littleEndian);
			uint count = ReadUInt32(header, uInt32Term + 4, littleEndian);
			uint uInt324 = ReadUInt32(header, uInt32Term + 8, littleEndian);
			switch (uInt163)
			{
			case 256:
				uInt322 = (int)((uInt164 == 3) ? (uInt324 & 0xFFFF) : uInt324);
				break;
			case 257:
				uInt323 = (int)((uInt164 == 3) ? (uInt324 & 0xFFFF) : uInt324);
				break;
			case 33550:
				doubleArray = ReadDoubleArray(header, (int)uInt324, (int)count, littleEndian);
				if (doubleArray == null)
				{
					condition = true;
				}
				break;
			case 33922:
				doubleArrayArray = ReadDoubleArray(header, (int)uInt324, (int)count, littleEndian);
				if (doubleArrayArray == null)
				{
					condition = true;
				}
				break;
			case 34735:
				uInt16ArrayArray = ReadUInt16Array(header, (int)uInt324, (int)count, littleEndian);
				if (uInt16ArrayArray == null)
				{
					condition = true;
				}
				break;
			case 34736:
				ReadDoubleArray(header, (int)uInt324, (int)count, littleEndian);
				break;
			}
		}
		if (condition)
		{
			return (Bounds: default, Success: false, NeedsMoreData: true);
		}
		if (uInt16ArrayArray != null && uInt16ArrayArray.Length >= 4)
		{
			int uInt16Array3 = uInt16ArrayArray[3];
			for (int j = 0; j < uInt16Array3 && 4 + (j + 1) * 4 <= uInt16ArrayArray.Length; j++)
			{
				int ordinal = 4 + j * 4;
				ushort uInt16Array4 = uInt16ArrayArray[ordinal];
				ushort uInt16Array5 = uInt16ArrayArray[ordinal + 1];
				_ = uInt16ArrayArray[ordinal + 2];
				ushort uInt16Array6 = uInt16ArrayArray[ordinal + 3];
				if (uInt16Array4 == 3072 && uInt16Array5 == 0)
				{
					uInt16Array = uInt16Array6;
				}
				else if (uInt16Array4 == 2048 && uInt16Array5 == 0)
				{
					uInt16Array2 = uInt16Array6;
				}
			}
		}
		if (uInt322 == 0 || uInt323 == 0 || doubleArray == null || doubleArrayArray == null)
		{
			_logger.LogTrace("Missing required GeoTIFF tags for {Url}", url);
			return (Bounds: default, Success: false, NeedsMoreData: false);
		}
		double doubleArray2 = doubleArrayArray[3];
		double doubleArray3 = doubleArrayArray[4];
		double doubleArray4 = doubleArray[0];
		double doubleArray5 = doubleArray[1];
		Extent extent = new Extent(doubleArray2, doubleArray2 + (double)uInt322 * doubleArray4, doubleArray3 - (double)uInt323 * doubleArray5, doubleArray3);
		Projection projection = null;
		if (uInt16Array > 0 && uInt16Array < 32767)
		{
			projection = EPSGProjection.GetOrMake(uInt16Array);
		}
		else if (uInt16Array2 > 0 && uInt16Array2 < 32767)
		{
			projection = EPSGProjection.GetOrMake(uInt16Array2);
		}
		if (projection == null || uInt16Array2 == 4326 || uInt16Array2 == 4269)
		{
			return (Bounds: extent, Success: true, NeedsMoreData: false);
		}
		try
		{
			ReprojectionTransformer orMake = ReprojectionTransformer.GetOrMake(projection, WellKnownProjections.WGS84);
			return (Bounds: Projector.GetBoundingBox(extent, orMake), Success: true, NeedsMoreData: false);
		}
		catch (Exception exception)
		{
			_logger.LogTrace(exception, "Failed to reproject extent to WGS84, using native extent");
			return (Bounds: extent, Success: true, NeedsMoreData: false);
		}
	}

	private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, bool littleEndian)
	{
		if (offset + 2 > data.Length)
		{
			return 0;
		}
		if (!littleEndian)
		{
			return BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset));
		}
		return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
	}

	private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, bool littleEndian)
	{
		if (offset + 4 > data.Length)
		{
			return 0u;
		}
		if (!littleEndian)
		{
			return BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
		}
		return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset));
	}

	private static double ReadDouble(ReadOnlySpan<byte> data, int offset, bool littleEndian)
	{
		if (offset + 8 > data.Length)
		{
			return 0.0;
		}
		if (!littleEndian)
		{
			return BinaryPrimitives.ReadDoubleBigEndian(data.Slice(offset));
		}
		return BinaryPrimitives.ReadDoubleLittleEndian(data.Slice(offset));
	}

	private static double[] ReadDoubleArray(ReadOnlySpan<byte> data, int offset, int count, bool littleEndian)
	{
		if (offset + count * 8 > data.Length)
		{
			return null;
		}
		double[] doubleValues = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = ReadDouble(data, offset + i * 8, littleEndian);
		}
		return doubleValues;
	}

	private static ushort[] ReadUInt16Array(ReadOnlySpan<byte> data, int offset, int count, bool littleEndian)
	{
		if (offset + count * 2 > data.Length)
		{
			return null;
		}
		ushort[] ushorts = new ushort[count];
		for (int i = 0; i < count; i++)
		{
			ushorts[i] = ReadUInt16(data, offset + i * 2, littleEndian);
		}
		return ushorts;
	}
}
