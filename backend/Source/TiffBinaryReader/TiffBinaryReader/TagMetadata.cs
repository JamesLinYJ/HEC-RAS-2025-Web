using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TiffBinaryReader.TagTypeEnums;
using Utility;
using Utility.Helpers;
using Utility.Logging;
using Utility.Memory;

namespace TiffBinaryReader;

public class TagMetadata
{
	public sealed class TypeJsonConverter : JsonConverter<Type>
	{
		public override Type Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType == JsonTokenType.Null)
			{
				return null;
			}
			if (reader.TokenType != JsonTokenType.String)
			{
				throw new JsonException("Expected string for System.Type.");
			}
			string getString = reader.GetString();
			if (string.IsNullOrWhiteSpace(getString))
			{
				return null;
			}
			Type? type = System.Type.GetType(getString, throwOnError: false);
			if (type == null)
			{
				throw new JsonException("Unknown type '" + getString + "'.");
			}
			return type;
		}

		public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
		{
			if ((object)value == null)
			{
				writer.WriteNullValue();
			}
			else
			{
				writer.WriteStringValue(value.AssemblyQualifiedName);
			}
		}
	}

	public class MessageCodes
	{
		public static readonly MessageClass MandatoryTagMissing = new MessageClass("Tiff_Tag_Missing", MessageSeverity.Error, "File missing mandatory tag '{TiffTag}'.");

		public static Message GetMandatoryTagMissing(TagType tt)
		{
			return MandatoryTagMissing.Create(tt.Name);
		}
	}

	private const int BytesPerBigTIFFTag = 20;

	private const int BytesPerRegTIFFTag = 12;

	private static readonly ILogger _logger = LogHelper.GetLogger("TagMetadata");

	private readonly List<Tag> _tags = new List<Tag>();

	private readonly List<UnknownTag> _unknownTags = new List<UnknownTag>();

	[JsonPropertyName("tileOffsetTag")]
	public Tag TileOffsetTag;

	[JsonPropertyName("tileByteCountTag")]
	public Tag TileByteCountTag;

	private long[] _tileByteOffsets;

	private long[] _tileByteCounts;

	private byte[] _tagBytes;

	private readonly bool _isBigTiff;

	private object _ndvAsT;

	private const int MaxUnknownExternalBytes = 65536;

	public IReadOnlyList<Tag> Tags => _tags;

	public IReadOnlyList<UnknownTag> UnknownTags => _unknownTags;

	[JsonPropertyName("canReadData")]
	public bool CanReadData { get; private set; }

	public OperationResult CanReadErrors { get; private set; }

	[JsonPropertyName("errorStatus")]
	public string ErrorStatus => CanReadErrors.GetConcatenatedMessages();

	[JsonPropertyName("directoryOffset")]
	public long DirectoryOffset { get; internal set; }

	[JsonPropertyName("tagCount")]
	public long TagCount { get; internal set; }

	[JsonPropertyName("nextDirectoryOffset")]
	public long NextDirectoryOffset { get; internal set; }

	[JsonPropertyName("imageWidth")]
	public long ImageWidth { get; internal set; } = long.MinValue;

	[JsonPropertyName("imageHeight")]
	public long ImageHeight { get; internal set; } = long.MinValue;

	[JsonPropertyName("bitsPerSample")]
	public int BitsPerSample { get; internal set; } = int.MinValue;

	[JsonPropertyName("samplesPerPixel")]
	public int SamplesPerPixel { get; internal set; } = int.MinValue;

	public int BytesPerPixel => SamplesPerPixel * BitsPerSample / 8;

	[JsonPropertyName("sampleFormat")]
	public SampleFormats SampleFormat { get; internal set; } = SampleFormats.IEEEFP;

	[JsonPropertyName("orientation")]
	public Orientations Orientation { get; internal set; } = Orientations.TopLeft;

	[JsonPropertyName("planarConfiguration")]
	public PlanarConfigs PlanarConfiguration { get; internal set; } = PlanarConfigs.Contiguous;

	[JsonPropertyName("subfileType")]
	public SubfileTypes SubfileType { get; internal set; }

	[JsonPropertyName("photoInterpretation")]
	public PhotometricInterpretations PhotoInterpretation { get; set; }

	[JsonPropertyName("extraSamples")]
	public ExtraSampleTypes[] ExtraSamples { get; set; }

	[JsonPropertyName("isTiled")]
	public bool IsTiled { get; internal set; }

	[JsonPropertyName("isCompressed")]
	public bool IsCompressed => CompressionType != CompressionTypes.Uncompressed;

	[JsonPropertyName("compressionType")]
	public CompressionTypes CompressionType { get; internal set; } = CompressionTypes.Uncompressed;

	[JsonPropertyName("predictor")]
	public Predictors Predictor { get; internal set; } = Predictors.None;

	[JsonPropertyName("fillOrder")]
	public FillOrders FillOrder { get; internal set; } = FillOrders.MSBFirst;

	[JsonPropertyName("noData")]
	public string NoData { get; internal set; }

	[JsonPropertyName("gdalMetadata")]
	public string GDALMetadata { get; internal set; }

	[JsonPropertyName("tileWidth")]
	public int TileWidth { get; internal set; } = int.MinValue;

	[JsonPropertyName("tileHeight")]
	public int TileHeight { get; internal set; } = int.MinValue;

	[JsonPropertyName("tilesWide")]
	public int TilesWide
	{
		get
		{
			if (!IsTiled)
			{
				return 0;
			}
			return (int)Math.Ceiling((float)ImageWidth / (float)TileWidth);
		}
	}

	[JsonPropertyName("tilesTall")]
	public int TilesTall
	{
		get
		{
			if (!IsTiled)
			{
				return 0;
			}
			return (int)Math.Ceiling((float)ImageHeight / (float)TileHeight);
		}
	}

	[JsonPropertyName("tileCount")]
	public int TileCount => TilesWide * TilesTall;

	[JsonPropertyName("numberCellsPerTile")]
	public int NumberCellsPerTile
	{
		get
		{
			if (!IsTiled)
			{
				return 0;
			}
			return TileWidth * TileHeight;
		}
	}

	[JsonPropertyName("left")]
	public double Left { get; internal set; } = double.NaN;

	[JsonPropertyName("top")]
	public double Top { get; internal set; } = double.NaN;

	[JsonPropertyName("cellSize")]
	public double CellSize { get; internal set; } = double.NaN;

	private int BytesPerTag
	{
		get
		{
			if (_isBigTiff)
			{
				return 20;
			}
			return 12;
		}
	}

	[JsonPropertyName("parsedType")]
	[JsonConverter(typeof(TypeJsonConverter))]
	public Type ParsedType { get; private set; }

	public long TileByteOffset(int index)
	{
		if (TileOffsetTag.IsNativeAs<long>())
		{
			return TileOffsetTag.ValueAs<long>(index);
		}
		if (TileOffsetTag.IsNativeAs<ulong>())
		{
			return (long)TileOffsetTag.ValueAs<ulong>(index);
		}
		long[] tileByteOffsets = _tileByteOffsets;
		return ((tileByteOffsets != null) ? ((Span<long>)tileByteOffsets) : TileOffsetTag.ValuesAs<long>())[index];
	}

	public long TileByteCount(int index)
	{
		long[] tileByteCounts = _tileByteCounts;
		return ((tileByteCounts != null) ? ((Span<long>)tileByteCounts) : TileByteCountTag.ValuesAs<long>())[index];
	}

	private void LoadTileMetadata()
	{
		if (TileOffsetTag != null && TileByteCountTag != null)
		{
			if (!TileOffsetTag.IsNativeAs<long>())
			{
				_tileByteOffsets = new long[TileOffsetTag.ValueCount];
				TileOffsetTag.TryGetAs(_tileByteOffsets);
			}
			if (!TileByteCountTag.IsNativeAs<long>())
			{
				_tileByteCounts = new long[TileByteCountTag.ValueCount];
				TileByteCountTag.TryGetAs(_tileByteCounts);
			}
		}
	}

	private static void TryGetAsLong(Tag tag, long[] buffer)
	{
		if (tag.IsNativeAs<ulong>())
		{
			Span<ulong> valuesAs = tag.ValuesAs<ulong>();
			for (int i = 0; i < tag.ValueCount; i++)
			{
				buffer[i] = (long)valuesAs[i];
			}
		}
		else if (tag.IsNativeAs<uint>())
		{
			Span<uint> valuesAsSpan = tag.ValuesAs<uint>();
			for (int j = 0; j < tag.ValueCount; j++)
			{
				buffer[j] = valuesAsSpan[j];
			}
		}
		else if (tag.IsNativeAs<int>())
		{
			Span<int> valuesAs2 = tag.ValuesAs<int>();
			for (int k = 0; k < tag.ValueCount; k++)
			{
				buffer[k] = valuesAs2[k];
			}
		}
		else
		{
			tag.ValuesAs<long>().CopyTo(buffer);
		}
	}

	public static TagMetadata Read(BinaryReader br, long dirStartPosition, bool isBigTiff)
	{
		return new TagMetadata(br, dirStartPosition, isBigTiff);
	}

	public static async Task<TagMetadata> ReadAsync(Stream stream, long dirStartPosition, bool isBigTiff, CancellationToken ct = default(CancellationToken))
	{
		TagMetadata meta = new TagMetadata(isBigTiff)
		{
			DirectoryOffset = dirStartPosition
		};
		stream.Position = dirStartPosition;
		if (isBigTiff)
		{
			byte[] tagCountBuf = new byte[8];
			await stream.ReadExactlyAsync(tagCountBuf, ct);
			meta.TagCount = (long)BitConverter.ToUInt64(tagCountBuf, 0);
			meta._tagBytes = new byte[(int)meta.TagCount * 20];
			await stream.ReadExactlyAsync(meta._tagBytes, ct);
			byte[] nextDirBuf = new byte[8];
			await stream.ReadExactlyAsync(nextDirBuf, ct);
			meta.NextDirectoryOffset = (long)BitConverter.ToUInt64(nextDirBuf, 0);
		}
		else
		{
			byte[] nextDirBuf = new byte[2];
			await stream.ReadExactlyAsync(nextDirBuf, ct);
			meta.TagCount = BitConverter.ToUInt16(nextDirBuf, 0);
			meta._tagBytes = new byte[(int)meta.TagCount * 12];
			await stream.ReadExactlyAsync(meta._tagBytes, ct);
			byte[] tagCountBuf = new byte[4];
			await stream.ReadExactlyAsync(tagCountBuf, ct);
			meta.NextDirectoryOffset = BitConverter.ToUInt32(tagCountBuf, 0);
		}
		await meta.ImportTagsAsync(stream, ct);
		OperationResult operationResult = (meta.CanReadErrors = meta.ProcessTagMetadata());
		meta.CanReadData = operationResult.Result;
		return meta;
	}

	private TagMetadata(BinaryReader br, long dirStartPosition, bool isBigTiff)
		: this(isBigTiff)
	{
		DirectoryOffset = dirStartPosition;
		br.BaseStream.Position = dirStartPosition;
		if (_isBigTiff)
		{
			TagCount = (long)br.ReadUInt64();
			_tagBytes = br.ReadBytes((int)TagCount * BytesPerTag);
			NextDirectoryOffset = (long)br.ReadUInt64();
		}
		else
		{
			TagCount = br.ReadUInt16();
			_tagBytes = br.ReadBytes((int)TagCount * BytesPerTag);
			NextDirectoryOffset = br.ReadUInt32();
		}
		ImportTags(br);
		CanReadData = (CanReadErrors = ProcessTagMetadata()).Result;
	}

	private TagMetadata(bool isBigTiff)
	{
		_isBigTiff = isBigTiff;
	}

	public static TagMetadata ForWriting<T>(long imgWidth, long imgHeight, T? noDataValue) where T : unmanaged
	{
		return ForWriting(imgWidth, imgHeight, isOverlay: false, noDataValue, 1, PlanarConfigs.Contiguous);
	}

	public static TagMetadata ForWriting<T>(long imgWidth, long imgHeight, bool isOverlay, T? noDataValue) where T : unmanaged
	{
		return ForWriting(imgWidth, imgHeight, isOverlay, noDataValue, 1, PlanarConfigs.Contiguous);
	}

	public static TagMetadata ForImage(long imgWidth, long imgHeight)
	{
		TagMetadata tagMetadata = ForWriting<byte>(imgWidth, imgHeight, isOverlay: false, null, 3, PlanarConfigs.Contiguous);
		tagMetadata.PhotoInterpretation = PhotometricInterpretations.RGB;
		tagMetadata.SampleFormat = SampleFormats.UInt;
		return tagMetadata;
	}

	public static TagMetadata ForImageRGBA(long imgWidth, long imgHeight)
	{
		TagMetadata tagMetadata = ForWriting<byte>(imgWidth, imgHeight, isOverlay: false, null, 4, PlanarConfigs.Contiguous);
		tagMetadata.PhotoInterpretation = PhotometricInterpretations.RGB;
		tagMetadata.ExtraSamples = new ExtraSampleTypes[1] { ExtraSampleTypes.UnassociatedAlpha };
		tagMetadata.SampleFormat = SampleFormats.UInt;
		return tagMetadata;
	}

	public static TagMetadata ForWriting<T>(long imgWidth, long imgHeight, bool isOverlay, T? noDataValue, int samplesPerPixel, PlanarConfigs multiSampleConfig) where T : unmanaged
	{
		Type typeFromHandle = typeof(T);
		string noDataString = (noDataValue.HasValue ? Convert.ToString(noDataValue.Value, CultureInfo.InvariantCulture) : "");
		if (typeFromHandle == typeof(float))
		{
			return ForWriting(imgWidth, imgHeight, isOverlay, noDataString, SampleFormats.IEEEFP, 32, samplesPerPixel, multiSampleConfig);
		}
		if (typeFromHandle == typeof(double))
		{
			return ForWriting(imgWidth, imgHeight, isOverlay, noDataString, SampleFormats.IEEEFP, 64, samplesPerPixel, multiSampleConfig);
		}
		if (typeFromHandle == typeof(int))
		{
			return ForWriting(imgWidth, imgHeight, isOverlay, noDataString, SampleFormats.Int, 32, samplesPerPixel, multiSampleConfig);
		}
		if (typeFromHandle == typeof(short))
		{
			return ForWriting(imgWidth, imgHeight, isOverlay, noDataString, SampleFormats.Int, 16, samplesPerPixel, multiSampleConfig);
		}
		if (typeFromHandle == typeof(byte))
		{
			return ForWriting(imgWidth, imgHeight, isOverlay, noDataString, SampleFormats.Int, 8, samplesPerPixel, multiSampleConfig);
		}
		if (typeFromHandle == typeof(long))
		{
			return ForWriting(imgWidth, imgHeight, isOverlay, noDataString, SampleFormats.Int, 64, samplesPerPixel, multiSampleConfig);
		}
		throw new NotImplementedException();
	}

	public static TagMetadata ForWriting(long imgWidth, long imgHeight, bool isOverlay, string noDataString, SampleFormats dataFormat, int bitsPerSample, int samplesPerPixel, PlanarConfigs multiSampleConfig)
	{
		TagMetadata tagMetadata = new TagMetadata(isBigTiff: true);
		tagMetadata.ImageHeight = imgHeight;
		tagMetadata.ImageWidth = imgWidth;
		tagMetadata.NoData = noDataString;
		if (isOverlay)
		{
			tagMetadata.SubfileType = SubfileTypes.ReducedImage;
		}
		tagMetadata.CompressionType = CompressionTypes.Deflate;
		tagMetadata.IsTiled = true;
		tagMetadata.PhotoInterpretation = PhotometricInterpretations.BlackIsZero;
		tagMetadata.SampleFormat = dataFormat;
		tagMetadata.BitsPerSample = bitsPerSample;
		tagMetadata.SamplesPerPixel = samplesPerPixel;
		tagMetadata.PlanarConfiguration = multiSampleConfig;
		tagMetadata.TileWidth = 256;
		tagMetadata.TileHeight = 256;
		return tagMetadata;
	}

	public long AppendDirectory(Stream fs, List<Tag> extraTags, long[] tileByteOffsets, long[] tileByteCounts)
	{
		_tags.Clear();
		_tags.Add(Tag.CreateTag(TagTypes.ImageWidth, DataTypes.UInt, (uint)ImageWidth));
		_tags.Add(Tag.CreateTag(TagTypes.ImageLength, DataTypes.UInt, (uint)ImageHeight));
		_tags.Add(Tag.CreateTag(TagTypes.BitsPerSample, DataTypes.UInt, (uint)BitsPerSample));
		_tags.Add(Tag.CreateTag(TagTypes.SamplesPerPixel, DataTypes.UInt, (uint)SamplesPerPixel));
		_tags.Add(Tag.CreateTag(TagTypes.SampleFormat, DataTypes.UShort, (ushort)SampleFormat));
		_tags.Add(Tag.CreateTag(TagTypes.FillOrder, DataTypes.UShort, (ushort)FillOrder));
		_tags.Add(Tag.CreateTag(TagTypes.Compression, DataTypes.UShort, (ushort)CompressionType));
		_tags.Add(Tag.CreateTag(TagTypes.Orientation, DataTypes.UShort, (ushort)Orientation));
		_tags.Add(Tag.CreateTag(TagTypes.SubfileType, DataTypes.UShort, (ushort)SubfileType));
		_tags.Add(Tag.CreateTag(TagTypes.PlanarConfig, DataTypes.UShort, (ushort)PlanarConfiguration));
		_tags.Add(Tag.CreateTag(TagTypes.PhotometricInterpID, DataTypes.UShort, (ushort)PhotoInterpretation));
		if (ExtraSamples != null && ExtraSamples.Length != 0)
		{
			Span<ushort> mirroredMemory = MemoryHelpers.CreateMirroredMemory<ushort>(ExtraSamples.Length, out var other);
			for (int i = 0; i < ExtraSamples.Length; i++)
			{
				mirroredMemory[i] = (ushort)ExtraSamples[i];
			}
			_tags.Add(new Tag(TagTypes.ExtraSamples, DataTypes.UShort, other));
		}
		if (NoData != "")
		{
			_tags.Add(Tag.CreateTag(TagTypes.GDALNoData, DataTypes.Ascii, NoData));
		}
		_tags.Add(Tag.CreateTag(TagTypes.TileWidth, DataTypes.UInt, (uint)TileWidth));
		_tags.Add(Tag.CreateTag(TagTypes.TileLength, DataTypes.UInt, (uint)TileHeight));
		int ct = tileByteOffsets.Length;
		Span<ulong> mirroredMemorySpan = MemoryHelpers.CreateMirroredMemory<ulong>(ct, out var other2);
		for (int j = 0; j < mirroredMemorySpan.Length; j++)
		{
			mirroredMemorySpan[j] = (ulong)tileByteOffsets[j];
		}
		_tags.Add(new Tag(TagTypes.TileByteOffsets, DataTypes.ULong8, other2));
		Span<ulong> mirroredMemory2 = MemoryHelpers.CreateMirroredMemory<ulong>(ct, out var other3);
		for (int k = 0; k < mirroredMemory2.Length; k++)
		{
			mirroredMemory2[k] = (ulong)tileByteCounts[k];
		}
		_tags.Add(new Tag(TagTypes.TileByteCounts, DataTypes.ULong8, other3));
		if (!double.IsNaN(Left) && !double.IsNaN(Top) && !double.IsNaN(CellSize))
		{
			_tags.Add(CreateLocationTag(Left, Top));
			_tags.Add(CreateScaleTag(CellSize));
		}
		if (extraTags != null)
		{
			_tags.AddRange(extraTags);
		}
		TagCount = _tags.Count;
		byte[] byteValues = new byte[BytesPerTag * TagCount];
		new List<byte>();
		int bytesPerTagSum = 0;
		long bytes3LengthSum = fs.Length + 16 + byteValues.Length;
		List<Memory<byte>> byteSpanList = new List<Memory<byte>>();
		foreach (Tag orderBy in Tags.OrderBy((Tag t) => t.TagType.ID))
		{
			byte[] bytes = BitConverter.GetBytes(orderBy.TagType.ID);
			byteValues[bytesPerTagSum] = bytes[0];
			byteValues[bytesPerTagSum + 1] = bytes[1];
			byte[] bytes2 = BitConverter.GetBytes(orderBy.ActualDataType.UID);
			byteValues[bytesPerTagSum + 2] = bytes2[0];
			byteValues[bytesPerTagSum + 3] = bytes2[1];
			Memory<byte> bytes3 = orderBy.GetBytes();
			byte[] bytes4 = BitConverter.GetBytes((ulong)(bytes3.Length / orderBy.ActualDataType.ByteCount));
			byteValues[bytesPerTagSum + 4] = bytes4[0];
			byteValues[bytesPerTagSum + 5] = bytes4[1];
			byteValues[bytesPerTagSum + 6] = bytes4[2];
			byteValues[bytesPerTagSum + 7] = bytes4[3];
			byteValues[bytesPerTagSum + 8] = bytes4[4];
			byteValues[bytesPerTagSum + 9] = bytes4[5];
			byteValues[bytesPerTagSum + 10] = bytes4[6];
			byteValues[bytesPerTagSum + 11] = bytes4[7];
			if (bytes3.Length <= 8)
			{
				for (int intervalIndex = 0; intervalIndex < bytes3.Length; intervalIndex++)
				{
					byteValues[bytesPerTagSum + 12 + intervalIndex] = bytes3.Span[intervalIndex];
				}
			}
			else
			{
				byteSpanList.Add(bytes3);
				byte[] bytes5 = BitConverter.GetBytes((ulong)bytes3LengthSum);
				for (int intervalIndex2 = 0; intervalIndex2 < bytes5.Length; intervalIndex2++)
				{
					byteValues[bytesPerTagSum + 12 + intervalIndex2] = bytes5[intervalIndex2];
				}
				bytes3LengthSum += bytes3.Length;
			}
			bytesPerTagSum += BytesPerTag;
		}
		DirectoryOffset = fs.Length;
		fs.Seek(0L, SeekOrigin.End);
		byte[] bytes6 = BitConverter.GetBytes((ulong)TagCount);
		fs.Write(bytes6, 0, bytes6.Length);
		fs.Write(byteValues, 0, byteValues.Length);
		long length = fs.Length;
		byte[] bytes7 = BitConverter.GetBytes(0uL);
		fs.Write(bytes7, 0, bytes7.Length);
		foreach (Memory<byte> localValue in byteSpanList)
		{
			fs.Write(localValue.Span);
		}
		return length;
	}

	public Memory<byte> GetUncompressedNoDataTile()
	{
		int bitsPerSample = BitsPerSample;
		int samplesPerPixel = SamplesPerPixel;
		int nElements = samplesPerPixel * NumberCellsPerTile;
		int count = bitsPerSample * nElements / 8;
		Memory<byte> retn = new byte[count];
		if (string.IsNullOrEmpty(NoData))
		{
			_logger.LogWarning("No NoDataValue provided for GetUncompressedNoDataTile.");
			return retn;
		}
		UseAsDataType(NoData, (byte b) =>
		{
			retn.Span.Fill(b);
		}, (short s) =>
		{
			Memory<short> source = new short[nElements];
			source.Span.Fill(s);
			Buffers.ArrayCopyUnsafe(source, retn);
		}, (int v) =>
		{
			Memory<int> source = new int[nElements];
			source.Span.Fill(v);
			Buffers.ArrayCopyUnsafe(source, retn);
		}, (long v) =>
		{
			Memory<long> source = new long[nElements];
			source.Span.Fill(v);
			Buffers.ArrayCopyUnsafe(source, retn);
		}, (float v) =>
		{
			Memory<float> source = new float[nElements];
			source.Span.Fill(v);
			Buffers.ArrayCopyUnsafe(source, retn);
		}, (double v) =>
		{
			Memory<double> source = new double[nElements];
			source.Span.Fill(v);
			Buffers.ArrayCopyUnsafe(source, retn);
		}, () =>
		{
			_logger.LogWarning("Parse Error for {NoData} in GetUncompressedNoDataTile", NoData);
		});
		return retn;
	}

	public bool TryGetNoDataAsValue<T>(out T el2) where T : unmanaged
	{
		bool found = false;
		T el = default;
		UseAsDataType(NoData, (byte b) =>
		{
			el = (b as T?).GetValueOrDefault();
			found = true;
		}, (short b) =>
		{
			el = (b as T?).GetValueOrDefault();
			found = true;
		}, (int b) =>
		{
			el = (b as T?).GetValueOrDefault();
			found = true;
		}, (long b) =>
		{
			el = (b as T?).GetValueOrDefault();
			found = true;
		}, (float b) =>
		{
			el = (b as T?).GetValueOrDefault();
			found = true;
		}, (double b) =>
		{
			el = (b as T?).GetValueOrDefault();
			found = true;
		}, () =>
		{
		});
		el2 = el;
		return found;
	}

	private Type ParseType()
	{
		if (SampleFormat == SampleFormats.IEEEFP)
		{
			if (BitsPerSample == 32)
			{
				return typeof(float);
			}
			if (BitsPerSample == 64)
			{
				return typeof(double);
			}
			return null;
		}
		if (SampleFormat == SampleFormats.Int)
		{
			if (BitsPerSample == 8)
			{
				return typeof(byte);
			}
			if (BitsPerSample == 16)
			{
				return typeof(short);
			}
			if (BitsPerSample == 32)
			{
				return typeof(int);
			}
			if (BitsPerSample == 64)
			{
				return typeof(long);
			}
			return null;
		}
		if (SampleFormat == SampleFormats.UInt)
		{
			if (BitsPerSample == 8)
			{
				return typeof(byte);
			}
			if (BitsPerSample == 16)
			{
				return typeof(ushort);
			}
			if (BitsPerSample == 32)
			{
				return typeof(uint);
			}
			if (BitsPerSample == 64)
			{
				return typeof(ulong);
			}
			return null;
		}
		return null;
	}

	public T? NoDataAs<T>() where T : unmanaged
	{
		object ndvAsT = _ndvAsT;
		if (ndvAsT != null && ndvAsT is T)
		{
			return (T)ndvAsT;
		}
		T? ndvAsT2 = ParseHelpers.TryParseStringAs_InvariantCulture<T>(NoData);
		_ndvAsT = ndvAsT2;
		return ndvAsT2;
	}

	public void UseAsDataType(string stringValue, Action<byte> onByte, Action<short> onShort, Action<int> onInt, Action<long> onLong, Action<float> onFloat, Action<double> onDouble, Action onParseError)
	{
		if (string.IsNullOrEmpty(stringValue))
		{
			onParseError();
			return;
		}
		CultureInfo invariantCulture = CultureInfo.InvariantCulture;
		if (SampleFormat == SampleFormats.IEEEFP)
		{
			if (BitsPerSample == 32)
			{
				if (float.TryParse(stringValue, NumberStyles.Float, invariantCulture, out var result))
				{
					onFloat(result);
				}
				else
				{
					onParseError();
				}
			}
			else if (BitsPerSample == 64)
			{
				if (double.TryParse(stringValue, NumberStyles.Float, invariantCulture, out var result2))
				{
					onDouble(result2);
				}
				else
				{
					onParseError();
				}
			}
			else
			{
				onParseError();
			}
		}
		else if (SampleFormat == SampleFormats.Int)
		{
			if (BitsPerSample == 8)
			{
				if (byte.TryParse(stringValue, NumberStyles.Integer, invariantCulture, out var result3))
				{
					onByte(result3);
				}
				else
				{
					onParseError();
				}
			}
			else if (BitsPerSample == 16)
			{
				if (short.TryParse(stringValue, NumberStyles.Integer, invariantCulture, out var result4))
				{
					onShort(result4);
				}
				else
				{
					onParseError();
				}
			}
			else if (BitsPerSample == 32)
			{
				if (int.TryParse(stringValue, NumberStyles.Integer, invariantCulture, out var result5))
				{
					onInt(result5);
				}
				else
				{
					onParseError();
				}
			}
			else if (BitsPerSample == 64)
			{
				if (long.TryParse(stringValue, NumberStyles.Integer, invariantCulture, out var result6))
				{
					onLong(result6);
				}
				else
				{
					onParseError();
				}
			}
			else
			{
				onParseError();
			}
		}
		else
		{
			onParseError();
		}
	}

	public void SetLocation(double left, double top, double cellSize)
	{
		Left = left;
		Top = top;
		CellSize = cellSize;
	}

	private static Tag CreateLocationTag(double minX, double maxY)
	{
		Span<double> mirroredMemory = MemoryHelpers.CreateMirroredMemory<double>(6, out var other);
		mirroredMemory[0] = 0.0;
		mirroredMemory[1] = 0.0;
		mirroredMemory[2] = 0.0;
		mirroredMemory[3] = minX;
		mirroredMemory[4] = maxY;
		mirroredMemory[5] = 0.0;
		return new Tag(TagTypes.ModelTiePoint, DataTypes.Double, other);
	}

	private static Tag CreateScaleTag(double cellSize)
	{
		Span<double> mirroredMemory = MemoryHelpers.CreateMirroredMemory<double>(3, out var other);
		mirroredMemory[0] = cellSize;
		mirroredMemory[1] = cellSize;
		mirroredMemory[2] = 0.0;
		return new Tag(TagTypes.ModelPixelScale, DataTypes.Double, other);
	}

	public string SerializeJson()
	{
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.WriteAsString
		};
		return JsonSerializer.Serialize(this, options);
	}

	private void ImportTags(BinaryReader br)
	{
		for (int i = 0; i < _tagBytes.Length; i += BytesPerTag)
		{
			byte[] byteValues = new byte[BytesPerTag];
			for (int j = 0; j < BytesPerTag; j++)
			{
				byteValues[j] = _tagBytes[j + i];
			}
			ushort tagIdx = BitConverter.ToUInt16(byteValues, 0);
			ushort toUInt16 = BitConverter.ToUInt16(byteValues, 2);
			ulong toUInt32 = 0uL;
			toUInt32 = ((!_isBigTiff) ? BitConverter.ToUInt32(byteValues, 4) : BitConverter.ToUInt64(byteValues, 4));
			DataType dataType = DataTypes.Get(toUInt16);
			TagType tagType = TagTypes.TagList.FirstOrDefault((TagType tt) => tt.ID == tagIdx);
			if (tagType == null)
			{
				_logger.LogDebug("Tag index {TagIndex} unknown, capturing as UnknownTag.", tagIdx);
				byte[] bytes = ReadRawValueBytes(dataType, (uint)toUInt32, byteValues, br, _isBigTiff);
				_unknownTags.Add(new UnknownTag(tagIdx, toUInt16, (long)toUInt32, bytes));
			}
			else if (!tagType.DataTypesSupported.Contains(dataType))
			{
				_logger.LogDebug("Tag index {TagIndex} shouldn't have data type {Description}, skipping.", tagIdx, dataType.Description);
			}
			else if (toUInt32 > tagType.MaxAllowableValues)
			{
				_logger.LogDebug("Tag index {TagIndex} can't support {DataCount} values, skipping.", tagIdx, toUInt32);
			}
			else
			{
				Tag createTag = CreateTag(tagType, dataType, (uint)toUInt32, byteValues, br, _isBigTiff);
				_tags.Add(createTag);
			}
		}
	}

	private async Task ImportTagsAsync(Stream stream, CancellationToken ct)
	{
		for (int i = 0; i < _tagBytes.Length; i += BytesPerTag)
		{
			byte[] byteValues = new byte[BytesPerTag];
			for (int j = 0; j < BytesPerTag; j++)
			{
				byteValues[j] = _tagBytes[j + i];
			}
			ushort tagIdx = BitConverter.ToUInt16(byteValues, 0);
			ushort dtID = BitConverter.ToUInt16(byteValues, 2);
			ulong dataCount = ((!_isBigTiff) ? BitConverter.ToUInt32(byteValues, 4) : BitConverter.ToUInt64(byteValues, 4));
			DataType dataType = DataTypes.Get(dtID);
			TagType tagType = TagTypes.TagList.FirstOrDefault((TagType tt) => tt.ID == tagIdx);
			if (tagType == null)
			{
				_logger.LogDebug("Tag index {TagIndex} unknown, capturing as UnknownTag.", tagIdx);
				byte[] bytes = await ReadRawValueBytesAsync(dataType, (uint)dataCount, byteValues, stream, _isBigTiff, ct);
				_unknownTags.Add(new UnknownTag(tagIdx, dtID, (long)dataCount, bytes));
			}
			else if (!tagType.DataTypesSupported.Contains(dataType))
			{
				_logger.LogDebug("Tag index {TagIndex} shouldn't have data type {Description}, skipping.", tagIdx, dataType.Description);
			}
			else if (dataCount > tagType.MaxAllowableValues)
			{
				_logger.LogDebug("Tag index {TagIndex} can't support {DataCount} values, skipping.", tagIdx, dataCount);
			}
			else
			{
				Tag tagAsync = await CreateTagAsync(tagType, dataType, (uint)dataCount, byteValues, stream, _isBigTiff, ct);
				_tags.Add(tagAsync);
			}
		}
	}

	private OperationResult ProcessTagMetadata()
	{
		OperationResult operationResult = true;
		Tag tag = FindTag(TagTypes.ImageWidth);
		if (tag == null)
		{
			operationResult.AddFailure(MessageCodes.GetMandatoryTagMissing(TagTypes.ImageWidth));
		}
		Tag tag2 = FindTag(TagTypes.ImageLength);
		if (tag2 == null)
		{
			operationResult.AddFailure(MessageCodes.GetMandatoryTagMissing(TagTypes.ImageLength));
		}
		Tag tag3 = FindTag(TagTypes.BitsPerSample);
		if (tag3 == null)
		{
			operationResult.AddFailure(MessageCodes.GetMandatoryTagMissing(TagTypes.BitsPerSample));
		}
		Tag tag4 = FindTag(TagTypes.SamplesPerPixel);
		if (tag4 == null)
		{
			operationResult.AddFailure(MessageCodes.GetMandatoryTagMissing(TagTypes.SamplesPerPixel));
		}
		Tag tag5 = FindTag(TagTypes.SampleFormat);
		if (tag5 == null)
		{
			operationResult.AddFailure(MessageCodes.GetMandatoryTagMissing(TagTypes.SampleFormat));
		}
		Tag tag6 = FindTag(TagTypes.FillOrder);
		Tag tag7 = FindTag(TagTypes.Compression);
		Tag tag8 = FindTag(TagTypes.Predictor);
		Tag tag9 = FindTag(TagTypes.Orientation);
		Tag tag10 = FindTag(TagTypes.SubfileType);
		Tag tag11 = FindTag(TagTypes.PlanarConfig);
		Tag tag12 = FindTag(TagTypes.PhotometricInterpID);
		Tag tag13 = FindTag(TagTypes.TileLength);
		Tag tag14 = FindTag(TagTypes.TileWidth);
		Tag tag15 = FindTag(TagTypes.TileByteOffsets);
		Tag tag16 = FindTag(TagTypes.TileByteCounts);
		Tag tag17 = FindTag(TagTypes.GDALNoData);
		Tag tag18 = FindTag(TagTypes.GDALMetadata);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		ImageWidth = tag.ValueAs<long>();
		ImageHeight = tag2.ValueAs<long>();
		SamplesPerPixel = tag4.ValueAs<int>();
		if (tag3.ValueCount != 1 && tag3.ValueCount != SamplesPerPixel)
		{
			operationResult.AddFailure($"BitsPerSample has {tag3.ValueCount} values; expected 1 or SamplesPerPixel ({SamplesPerPixel}).");
			return operationResult;
		}
		if (tag5.ValueCount != 1 && tag5.ValueCount != SamplesPerPixel)
		{
			operationResult.AddFailure($"SampleFormat has {tag5.ValueCount} values; expected 1 or SamplesPerPixel ({SamplesPerPixel}).");
			return operationResult;
		}
		BitsPerSample = tag3.ValueAs<int>();
		SampleFormat = (SampleFormats)tag5.ValueAs<ushort>();
		if (tag6 == null)
		{
			FillOrder = FillOrders.MSBFirst;
		}
		else
		{
			FillOrder = (FillOrders)tag6.ValueAs<ushort>();
		}
		if (tag10 == null)
		{
			SubfileType = SubfileTypes.None;
		}
		else
		{
			SubfileType = (SubfileTypes)tag10.ValueAs<ushort>();
		}
		if (tag12 == null)
		{
			PhotoInterpretation = PhotometricInterpretations.WhiteIsZero;
		}
		else
		{
			PhotoInterpretation = (PhotometricInterpretations)tag12.ValueAs<ushort>();
		}
		if (tag7 == null)
		{
			CompressionType = CompressionTypes.Uncompressed;
		}
		else
		{
			CompressionType = (CompressionTypes)tag7.ValueAs<ushort>();
		}
		if (tag8 == null)
		{
			Predictor = Predictors.None;
		}
		else
		{
			Predictor = (Predictors)tag8.ValueAs<ushort>();
		}
		if (tag9 == null)
		{
			Orientation = Orientations.TopLeft;
		}
		else
		{
			Orientation = (Orientations)tag9.ValueAs<ushort>();
		}
		if (tag11 == null)
		{
			PlanarConfiguration = PlanarConfigs.Contiguous;
		}
		else
		{
			PlanarConfiguration = (PlanarConfigs)tag11.ValueAs<ushort>();
		}
		if (tag17 != null)
		{
			NoData = tag17.ValueAsString();
		}
		if (tag18 != null)
		{
			GDALMetadata = tag18.ValueAsString();
		}
		if (tag13 != null)
		{
			TileHeight = tag13.ValueAs<int>();
		}
		if (tag14 != null)
		{
			TileWidth = tag14.ValueAs<int>();
		}
		TileOffsetTag = tag15;
		TileByteCountTag = tag16;
		LoadTileMetadata();
		if (tag13 != null && tag14 != null && tag15 != null && tag16 != null)
		{
			IsTiled = true;
		}
		else
		{
			IsTiled = false;
		}
		ParsedType = ParseType();
		OperationResult operationResult2 = ValidateCanRead();
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		return operationResult;
	}

	private OperationResult ValidateCanRead()
	{
		OperationResult operationResult = true;
		if (ImageHeight <= long.MinValue)
		{
			operationResult.AddFailure("ImageHeight invalid.");
		}
		if (ImageWidth <= 0)
		{
			operationResult.AddFailure("ImageWidth invalid.");
		}
		if (BitsPerSample <= 0)
		{
			operationResult.AddFailure("BitsPerSample invalid.");
		}
		if (SamplesPerPixel <= 0)
		{
			operationResult.AddFailure("SamplesPerPixel invalid.");
		}
		if (FillOrder != FillOrders.MSBFirst)
		{
			operationResult.AddFailure("FillOrder must be MSBFirst.");
		}
		if (SamplesPerPixel != 1)
		{
			operationResult.AddFailure("Only supports 1 SamplesPerPixel currently.");
		}
		if (!IsTiled)
		{
			operationResult.AddFailure("Data is not tiled.");
		}
		if (IsTiled && TileByteCountTag.ValueCount != TileOffsetTag.ValueCount)
		{
			operationResult.AddFailure("TileByteOffsets size is not equal to TileByteCounts size.");
		}
		if (Orientation != Orientations.TopLeft)
		{
			operationResult.AddFailure("Data is not top-left oriented.");
		}
		if (SamplesPerPixel != 1 && PlanarConfiguration != PlanarConfigs.Contiguous)
		{
			operationResult.AddFailure("Can only read Contiguous configurations, e.g. RGB.RGB.RGB.");
		}
		if (CompressionType != CompressionTypes.Deflate && CompressionType != CompressionTypes.Uncompressed)
		{
			if (CompressionType == CompressionTypes.LZW)
			{
				if (TiffDirectory.UseNewLzw)
				{
					OperationResult operationResult2 = LzwDecoder2CanDecode(this);
					if (!operationResult2)
					{
						operationResult.AddFailure(operationResult2);
					}
				}
				else
				{
					OperationResult operationResult3 = LZWCodec.CanDecode(this);
					if (!operationResult3)
					{
						operationResult.AddFailure(operationResult3);
					}
				}
			}
			else if (CompressionType != CompressionTypes.PackBits)
			{
				operationResult.AddFailure("CompressionType must be 'Deflate' or 'LZW'");
			}
		}
		return operationResult;
	}

	private static OperationResult LzwDecoder2CanDecode(TagMetadata meta)
	{
		OperationResult operationResult = new OperationResult();
		if (meta.CompressionType != CompressionTypes.LZW)
		{
			throw new Exception("Expected LZW compression type.");
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
		Predictor predictor = (Predictor)(meta.Tags.FirstOrDefault((Tag t) => t.TagType == TagTypes.Predictor)?.ValueAs<int>() ?? 0);
		if (predictor != TiffBinaryReader.Predictor.NONE && predictor != TiffBinaryReader.Predictor.HORIZONTAL && predictor != TiffBinaryReader.Predictor.FLOATINGPOINT)
		{
			operationResult.AddFailure("Predictor not recognized: " + predictor);
		}
		return operationResult;
	}

	private static Tag CreateTag(TagType tt, DataType dt, uint dataCount, Memory<byte> tagData, BinaryReader br, bool isBigTiff)
	{
		if (isBigTiff)
		{
			int count = (int)(dataCount * dt.ByteCount);
			if (count <= 8)
			{
				return new Tag(tt, dt, tagData.Slice(12, count));
			}
			long position = (long)BitConverter.ToUInt64(tagData.Span.Slice(12));
			br.BaseStream.Position = position;
			byte[] byteValues = new byte[count];
			br.Read(byteValues, 0, count);
			return new Tag(tt, dt, byteValues);
		}
		int count2 = (int)(dataCount * dt.ByteCount);
		if (count2 <= 4)
		{
			return new Tag(tt, dt, tagData.Slice(8, count2));
		}
		uint position2 = BitConverter.ToUInt32(tagData.Span.Slice(8));
		br.BaseStream.Position = position2;
		byte[] byteValuesArray = new byte[count2];
		br.Read(byteValuesArray, 0, count2);
		return new Tag(tt, dt, byteValuesArray);
	}

	private static async Task<Tag> CreateTagAsync(TagType tt, DataType dt, uint dataCount, Memory<byte> tagData, Stream stream, bool isBigTiff, CancellationToken ct)
	{
		byte[] buf;
		if (isBigTiff)
		{
			int count = (int)(dataCount * dt.ByteCount);
			if (count <= 8)
			{
				return new Tag(tt, dt, tagData.Slice(12, count));
			}
			long position = (long)BitConverter.ToUInt64(tagData.Span.Slice(12));
			stream.Position = position;
			buf = new byte[count];
			await stream.ReadExactlyAsync(buf, ct);
			return new Tag(tt, dt, buf);
		}
		int count2 = (int)(dataCount * dt.ByteCount);
		if (count2 <= 4)
		{
			return new Tag(tt, dt, tagData.Slice(8, count2));
		}
		uint position2 = BitConverter.ToUInt32(tagData.Span.Slice(8));
		stream.Position = position2;
		buf = new byte[count2];
		await stream.ReadExactlyAsync(buf, ct);
		return new Tag(tt, dt, buf);
	}

	private static byte[] ReadRawValueBytes(DataType dt, uint dataCount, byte[] tagDef, BinaryReader br, bool isBigTiff)
	{
		if (dt == null)
		{
			return Array.Empty<byte>();
		}
		int count = (int)dataCount * dt.ByteCount;
		if (count <= 0)
		{
			return Array.Empty<byte>();
		}
		int isBigTiffTerm = (isBigTiff ? 12 : 8);
		int isBigTiffTerm2 = (isBigTiff ? 8 : 4);
		if (count <= isBigTiffTerm2)
		{
			return tagDef.AsSpan(isBigTiffTerm, count).ToArray();
		}
		if (count > 65536)
		{
			return Array.Empty<byte>();
		}
		long position = (long)(isBigTiff ? BitConverter.ToUInt64(tagDef, isBigTiffTerm) : BitConverter.ToUInt32(tagDef, isBigTiffTerm));
		long position2 = br.BaseStream.Position;
		br.BaseStream.Position = position;
		byte[] byteValues = new byte[count];
		br.Read(byteValues, 0, count);
		br.BaseStream.Position = position2;
		return byteValues;
	}

	private static async Task<byte[]> ReadRawValueBytesAsync(DataType dt, uint dataCount, byte[] tagDef, Stream stream, bool isBigTiff, CancellationToken ct)
	{
		if (dt == null)
		{
			return Array.Empty<byte>();
		}
		int count = (int)dataCount * dt.ByteCount;
		if (count <= 0)
		{
			return Array.Empty<byte>();
		}
		int isBigTiffTerm = (isBigTiff ? 12 : 8);
		int isBigTiffTerm2 = (isBigTiff ? 8 : 4);
		if (count <= isBigTiffTerm2)
		{
			return tagDef.AsSpan(isBigTiffTerm, count).ToArray();
		}
		if (count > 65536)
		{
			return Array.Empty<byte>();
		}
		long position = (long)(isBigTiff ? BitConverter.ToUInt64(tagDef, isBigTiffTerm) : BitConverter.ToUInt32(tagDef, isBigTiffTerm));
		long saved = stream.Position;
		stream.Position = position;
		byte[] buf = new byte[count];
		await stream.ReadExactlyAsync(buf, ct);
		stream.Position = saved;
		return buf;
	}

	public Tag FindTag(TagType type)
	{
		return Tags.FirstOrDefault((Tag t) => t.TagType == type);
	}
}
