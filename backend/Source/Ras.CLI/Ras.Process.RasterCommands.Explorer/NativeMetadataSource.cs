using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using TiffBinaryReader;
using TiffBinaryReader.TagTypeEnums;
using Utility.Logging;

namespace Ras.Process.RasterCommands.Explorer;

public class NativeMetadataSource : IRasterMetadataSource, IDisposable
{
	private static readonly TagType[] EditableAsciiTags = new TagType[4]
	{
		TagTypes.DocumentName,
		TagTypes.ImageDescription,
		TagTypes.Software,
		TagTypes.Artist
	};

	private readonly string _filename;

	private TiffFile _tiff;

	private const int MaxGridDim = 8;

	public string DriverLabel => "Native TIFF";

	public string DisplayName { get; }

	public bool CanEditMetadata => true;

	public bool CanEditGeoreference => true;

	public bool SupportsTilePreview
	{
		get
		{
			if (_tiff.Directories.Count > 0)
			{
				return _tiff.Directories[0].Metadata.IsTiled;
			}
			return false;
		}
	}

	private TagMetadata BaseMeta => _tiff.Directories[0].Metadata;

	public NativeMetadataSource(string filename, string displayName)
	{
		_filename = filename;
		DisplayName = displayName;
		OperationResult operationResult = TiffFile.TryLoad(filename, out var file, out var _, out var _);
		if (!operationResult.Result || file == null || file.Directories.Count == 0)
		{
			file?.Dispose();
			throw new Exception("Native TIFF reader could not parse this file: " + operationResult.GetConcatenatedMessages());
		}
		_tiff = file;
	}

	public RasterSummary GetSummary()
	{
		TagMetadata baseMeta = BaseMeta;
		string dataType = baseMeta.ParsedType?.Name ?? $"{baseMeta.SampleFormat} {baseMeta.BitsPerSample}-bit";
		return new RasterSummary((int)baseMeta.ImageWidth, (int)baseMeta.ImageHeight, baseMeta.SamplesPerPixel, dataType);
	}

	public IReadOnlyList<SubdatasetEntry> GetSubdatasets()
	{
		return Array.Empty<SubdatasetEntry>();
	}

	public GeoreferenceInfo GetGeoreference()
	{
		TagMetadata baseMeta = BaseMeta;
		Tag tag = baseMeta.FindTag(TagTypes.ModelPixelScale);
		Tag tag2 = baseMeta.FindTag(TagTypes.ModelTiePoint);
		if (tag == null || tag2 == null || tag2.ValueCount < 6)
		{
			return new GeoreferenceInfo(HasGeoreference: false, double.NaN, double.NaN, double.NaN);
		}
		double cellSize = tag.ValueAs<double>();
		double originX = tag2.ValueAs<double>(3);
		double originY = tag2.ValueAs<double>(4);
		return new GeoreferenceInfo(HasGeoreference: true, originX, originY, cellSize);
	}

	public ProjectionInfo GetProjection()
	{
		TagMetadata baseMeta = BaseMeta;
		Tag tag = baseMeta.FindTag(TagTypes.GeoKeyDirectory);
		Tag tag2 = baseMeta.FindTag(TagTypes.GeoAsciiParams);
		if (tag == null && tag2 == null)
		{
			return new ProjectionInfo(HasProjection: false, "", 0, "", "", "");
		}
		string name = ((tag2 != null) ? tag2.ValueAsString().Trim(new char[2] { '|', ' ' }) : "(GeoTIFF keys present)");
		List<string> stringList = new List<string>();
		if (tag != null)
		{
			stringList.Add("GeoKeyDirectory");
		}
		if (baseMeta.FindTag(TagTypes.ModelPixelScale) != null)
		{
			stringList.Add("ModelPixelScale");
		}
		if (baseMeta.FindTag(TagTypes.ModelTiePoint) != null)
		{
			stringList.Add("ModelTiePoint");
		}
		if (baseMeta.FindTag(TagTypes.ModelTransformation) != null)
		{
			stringList.Add("ModelTransformation");
		}
		string extraDetail = "GeoTIFF tags: " + string.Join(", ", stringList);
		return new ProjectionInfo(HasProjection: true, name, 0, "", "", extraDetail);
	}

	public IReadOnlyList<MetadataEntry> GetMetadata()
	{
		TagMetadata baseMeta = BaseMeta;
		List<MetadataEntry> metadataEntries = new List<MetadataEntry>();
		foreach (KeyValuePair<string, string> parseGdalMetadataItem in ParseGdalMetadataItems(baseMeta.GDALMetadata))
		{
			metadataEntries.Add(new MetadataEntry(parseGdalMetadataItem.Key, parseGdalMetadataItem.Value, "GDALMetadata", IsEditable: true));
		}
		TagType[] editableAsciiTags = EditableAsciiTags;
		foreach (TagType tagType in editableAsciiTags)
		{
			Tag tag = baseMeta.FindTag(tagType);
			if (tag != null)
			{
				metadataEntries.Add(new MetadataEntry(tagType.Name, tag.ValueAsString(), "", IsEditable: true));
			}
		}
		if (!string.IsNullOrEmpty(baseMeta.NoData))
		{
			metadataEntries.Add(new MetadataEntry("NoData", baseMeta.NoData, "", IsEditable: false));
		}
		return metadataEntries;
	}

	public IReadOnlyList<PyramidLevelInfo> GetPyramidLevels()
	{
		GeoreferenceInfo georeference = GetGeoreference();
		double imageWidth = BaseMeta.ImageWidth;
		List<PyramidLevelInfo> pyramidLevelInfos = new List<PyramidLevelInfo>();
		IReadOnlyList<TiffDirectory> directories = _tiff.Directories;
		for (int i = 0; i < directories.Count; i++)
		{
			TagMetadata metadata = directories[i].Metadata;
			int imageWidth2 = (int)metadata.ImageWidth;
			int rows = (int)metadata.ImageHeight;
			double cellSize = double.NaN;
			Tag tag = metadata.FindTag(TagTypes.ModelPixelScale);
			if (tag != null)
			{
				cellSize = tag.ValueAs<double>();
			}
			else if (georeference.HasGeoreference && imageWidth2 > 0)
			{
				cellSize = georeference.CellSize * (imageWidth / (double)imageWidth2);
			}
			List<TiffTagEntry> tiffTagEntries = new List<TiffTagEntry>();
			foreach (Tag orderBy in metadata.Tags.OrderBy((Tag t) => t.TagType.ID))
			{
				tiffTagEntries.Add(FormatTag(orderBy));
			}
			foreach (UnknownTag orderBy2 in metadata.UnknownTags.OrderBy((UnknownTag u) => u.Id))
			{
				tiffTagEntries.Add(FormatUnknownTag(orderBy2));
			}
			pyramidLevelInfos.Add(new PyramidLevelInfo(i, imageWidth2, rows, cellSize, tiffTagEntries));
		}
		return pyramidLevelInfos;
	}

	private static TiffTagEntry FormatTag(Tag tag)
	{
		string name = tag.TagType?.Name ?? "(unknown)";
		string description = tag.TagType?.Description ?? "";
		int valueCount = tag.ValueCount;
		bool uidActualDataTypeTerm = tag.ActualDataType.UID == DataTypes.Ascii.UID;
		string valueAsStringTagTerm;
		try
		{
			if (uidActualDataTypeTerm)
			{
				valueAsStringTagTerm = tag.ValueAsString();
			}
			else
			{
				object[] values = tag.ActualDataType.Parse(tag.GetBytes().ToArray(), 0, (uint)valueCount);
				valueAsStringTagTerm = string.Join(", ", values);
			}
		}
		catch (Exception ex)
		{
			valueAsStringTagTerm = "(unreadable: " + ex.Message + ")";
		}
		string enumValue = (uidActualDataTypeTerm ? null : DescribeEnumValue(tag));
		if (!string.IsNullOrEmpty(enumValue))
		{
			valueAsStringTagTerm = valueAsStringTagTerm + " (" + enumValue + ")";
		}
		bool valueCountTerm = !uidActualDataTypeTerm && valueCount > 1;
		bool condition = valueAsStringTagTerm.Length > 40;
		string summary = ((valueCountTerm | condition) ? "..." : valueAsStringTagTerm);
		bool isEditableScalarType = valueCount == 1 && IsEditableScalarType(tag.ActualDataType);
		string editHint = (isEditableScalarType ? EnumOptionsFor(tag) : null);
		return new TiffTagEntry(name, summary, valueAsStringTagTerm, description, isEditableScalarType, tag.TagType.ID, editHint);
	}

	private static TiffTagEntry FormatUnknownTag(UnknownTag u)
	{
		string name = "Tag " + u.HexId;
		string formatValue = u.FormatValue();
		string description = $"Unrecognized TIFF tag (id {u.Id} / {u.HexId}, {u.TypeName}, {u.ValueCount} value(s)).";
		bool dataTypeIsNull = (u.DataType == null || u.DataType.UID != DataTypes.Ascii.UID) && u.ValueCount > 1;
		bool formatValueTerm = formatValue.Length > 40;
		string summary = ((dataTypeIsNull | formatValueTerm) ? "..." : formatValue);
		return new TiffTagEntry(name, summary, formatValue, description, IsEditable: false, u.Id, null);
	}

	private static string DescribeEnumValue(Tag tag)
	{
		if (tag.ValueCount != 1)
		{
			return null;
		}
		ushort valueAs;
		try
		{
			valueAs = (ushort)tag.ValueAs<int>();
		}
		catch
		{
			return null;
		}
		TagType tagType = tag.TagType;
		if (tagType == TagTypes.SubfileType)
		{
			SubfileTypes subfileTypes = (SubfileTypes)valueAs;
			return subfileTypes.ToString();
		}
		if (tagType == TagTypes.Compression)
		{
			return TagTypes.GetCompressionDescription(valueAs);
		}
		if (tagType == TagTypes.PhotometricInterpID)
		{
			return TagTypes.GetPhotometricDescription(valueAs);
		}
		if (tagType == TagTypes.FillOrder)
		{
			return TagTypes.GetFillOrderDescription(valueAs);
		}
		if (tagType == TagTypes.SampleFormat)
		{
			return EnumName<SampleFormats>(valueAs);
		}
		if (tagType == TagTypes.Orientation)
		{
			return EnumName<Orientations>(valueAs);
		}
		if (tagType == TagTypes.PlanarConfig)
		{
			return EnumName<PlanarConfigs>(valueAs);
		}
		if (tagType == TagTypes.Predictor)
		{
			return EnumName<Predictors>(valueAs);
		}
		return null;
	}

	private static string EnumName<TEnum>(ushort uInt16) where TEnum : struct, Enum
	{
		if (!Enum.IsDefined(typeof(TEnum), uInt16))
		{
			return "Unknown";
		}
		return Enum.GetName(typeof(TEnum), uInt16);
	}

	private static bool IsEditableScalarType(DataType dt)
	{
		if (dt != DataTypes.UByte && dt != DataTypes.Byte && dt != DataTypes.UShort && dt != DataTypes.Short && dt != DataTypes.UInt && dt != DataTypes.Int && dt != DataTypes.ULong8 && dt != DataTypes.Long8 && dt != DataTypes.Float)
		{
			return dt == DataTypes.Double;
		}
		return true;
	}

	private static bool TrySerializeScalar(DataType dt, string input, out byte[] bytes, out string error)
	{
		bytes = null;
		error = null;
		input = input?.Trim() ?? "";
		CultureInfo invariantCulture = CultureInfo.InvariantCulture;
		try
		{
			if (dt == DataTypes.UByte)
			{
				bytes = new byte[1] { byte.Parse(input, invariantCulture) };
			}
			else if (dt == DataTypes.Byte)
			{
				bytes = new byte[1] { (byte)sbyte.Parse(input, invariantCulture) };
			}
			else if (dt == DataTypes.UShort)
			{
				bytes = BitConverter.GetBytes(ushort.Parse(input, invariantCulture));
			}
			else if (dt == DataTypes.Short)
			{
				bytes = BitConverter.GetBytes(short.Parse(input, invariantCulture));
			}
			else if (dt == DataTypes.UInt)
			{
				bytes = BitConverter.GetBytes(uint.Parse(input, invariantCulture));
			}
			else if (dt == DataTypes.Int)
			{
				bytes = BitConverter.GetBytes(int.Parse(input, invariantCulture));
			}
			else if (dt == DataTypes.ULong8)
			{
				bytes = BitConverter.GetBytes(ulong.Parse(input, invariantCulture));
			}
			else if (dt == DataTypes.Long8)
			{
				bytes = BitConverter.GetBytes(long.Parse(input, invariantCulture));
			}
			else if (dt == DataTypes.Float)
			{
				bytes = BitConverter.GetBytes(float.Parse(input, NumberStyles.Float, invariantCulture));
			}
			else
			{
				if (dt != DataTypes.Double)
				{
					error = "Field type " + dt.Description + " can't be edited.";
					return false;
				}
				bytes = BitConverter.GetBytes(double.Parse(input, NumberStyles.Float, invariantCulture));
			}
			return true;
		}
		catch
		{
			error = $"'{input}' is not a valid {dt.Description} value.";
			return false;
		}
	}

	private static string EnumOptionsFor(Tag tag)
	{
		TagType tagType = tag.TagType;
		if (tagType == TagTypes.SubfileType)
		{
			return EnumOptions<SubfileTypes>();
		}
		if (tagType == TagTypes.Compression)
		{
			return EnumOptions<CompressionTypes>();
		}
		if (tagType == TagTypes.PhotometricInterpID)
		{
			return EnumOptions<PhotometricInterpretations>();
		}
		if (tagType == TagTypes.FillOrder)
		{
			return EnumOptions<FillOrders>();
		}
		if (tagType == TagTypes.SampleFormat)
		{
			return EnumOptions<SampleFormats>();
		}
		if (tagType == TagTypes.Orientation)
		{
			return EnumOptions<Orientations>();
		}
		if (tagType == TagTypes.PlanarConfig)
		{
			return EnumOptions<PlanarConfigs>();
		}
		if (tagType == TagTypes.Predictor)
		{
			return EnumOptions<Predictors>();
		}
		return null;
	}

	private static string EnumOptions<TEnum>() where TEnum : struct, Enum
	{
		return string.Join(", ", from v in Enum.GetValues<TEnum>()
			select $"{Convert.ToInt64(v)}={v}");
	}

	public bool TrySetMetadataItem(string key, string stringValue, out string error)
	{
		error = null;
		TagMetadata baseMeta = BaseMeta;
		long directoryOffset = baseMeta.DirectoryOffset;
		bool isBigTiff = _tiff.IsBigTiff;
		TagType tagType = EditableAsciiTags.FirstOrDefault((TagType t) => t.Name == key);
		_tiff.Dispose();
		_tiff = null;
		try
		{
			if (tagType != null)
			{
				return TiffMetadataEditor.TrySetAscii(_filename, directoryOffset, isBigTiff, tagType, stringValue, out error);
			}
			string gdalMetadataXml = BuildGdalMetadataXml(baseMeta.GDALMetadata, key, stringValue);
			return TiffMetadataEditor.TrySetAscii(_filename, directoryOffset, isBigTiff, TagTypes.GDALMetadata, gdalMetadataXml, out error);
		}
		finally
		{
			Reopen();
		}
	}

	public bool TrySetGeoreference(double originX, double originY, double cellSize, out string error)
	{
		error = null;
		if (cellSize <= 0.0)
		{
			error = "Cell size must be greater than zero.";
			return false;
		}
		TagMetadata baseMeta = BaseMeta;
		if (baseMeta.FindTag(TagTypes.ModelPixelScale) == null || baseMeta.FindTag(TagTypes.ModelTiePoint) == null)
		{
			error = "This file has no ModelPixelScale/ModelTiePoint tags (it may use a transformation matrix); can't edit georeferencing in place.";
			return false;
		}
		long directoryOffset = baseMeta.DirectoryOffset;
		bool isBigTiff = _tiff.IsBigTiff;
		double[] doubleValues = new double[3] { cellSize, cellSize, 0.0 };
		double[] doubleValuesArray = new double[6] { 0.0, 0.0, 0.0, originX, originY, 0.0 };
		_tiff.Dispose();
		_tiff = null;
		try
		{
			if (!TiffMetadataEditor.TrySetDoubles(_filename, directoryOffset, isBigTiff, TagTypes.ModelPixelScale, doubleValues, out error))
			{
				return false;
			}
			return TiffMetadataEditor.TrySetDoubles(_filename, directoryOffset, isBigTiff, TagTypes.ModelTiePoint, doubleValuesArray, out error);
		}
		finally
		{
			Reopen();
		}
	}

	public bool TrySetTiffTag(int levelIndex, int tagId, string stringValue, out string error)
	{
		error = null;
		if (levelIndex < 0 || levelIndex >= _tiff.Directories.Count)
		{
			error = "Invalid pyramid level.";
			return false;
		}
		TagMetadata metadata = _tiff.Directories[levelIndex].Metadata;
		Tag tag = metadata.Tags.FirstOrDefault((Tag t) => t.TagType.ID == tagId);
		if (tag == null)
		{
			error = "Tag not found on this level.";
			return false;
		}
		if (tag.ValueCount != 1 || !IsEditableScalarType(tag.ActualDataType))
		{
			error = "Only single-valued, well-known numeric tags can be edited in place.";
			return false;
		}
		if (!TrySerializeScalar(tag.ActualDataType, stringValue, out var bytes, out error))
		{
			return false;
		}
		long directoryOffset = metadata.DirectoryOffset;
		bool isBigTiff = _tiff.IsBigTiff;
		TagType tagType = tag.TagType;
		DataType actualDataType = tag.ActualDataType;
		_tiff.Dispose();
		_tiff = null;
		try
		{
			return TiffMetadataEditor.TryOverwriteTag(_filename, directoryOffset, isBigTiff, tagType, actualDataType, bytes, 1, out error);
		}
		finally
		{
			Reopen();
		}
	}

	public bool TryGetTilePreview(out TilePreview preview, out string error)
	{
		preview = null;
		error = null;
		TiffDirectory tiffDirectory = _tiff.Directories[0];
		TagMetadata metadata = tiffDirectory.Metadata;
		if (!metadata.IsTiled)
		{
			error = "Dataset is not tiled - no image preview.";
			return false;
		}
		if (!tiffDirectory.CanReadData)
		{
			error = "Native reader can't decode this directory's tiles.";
			return false;
		}
		if (metadata.ParsedType == null)
		{
			error = "Unsupported pixel data type.";
			return false;
		}
		int tilesWide = metadata.TilesWide;
		int tilesTall = metadata.TilesTall;
		if (tilesWide <= 0 || tilesTall <= 0)
		{
			error = "No tiles to preview.";
			return false;
		}
		int count = Math.Min(tilesWide, 8);
		int count2 = Math.Min(tilesTall, 8);
		double[] doubleValues = new double[count * count2];
		for (int i = 0; i < count2; i++)
		{
			int sampleIndex = SampleIndex(i, count2, tilesTall);
			for (int j = 0; j < count; j++)
			{
				int sampleIndex2 = SampleIndex(j, count, tilesWide);
				doubleValues[i * count + j] = ComputeTileDataFraction(tiffDirectory, sampleIndex * tilesWide + sampleIndex2);
			}
		}
		bool sampled = tilesWide > count || tilesTall > count2;
		preview = new TilePreview(tilesWide, tilesTall, count, count2, doubleValues, !string.IsNullOrEmpty(metadata.NoData), sampled);
		return true;
	}

	private static int SampleIndex(int displayIndex, int displayCount, int actualCount)
	{
		if (actualCount <= displayCount)
		{
			return displayIndex;
		}
		return (int)(((double)displayIndex + 0.5) * (double)actualCount / (double)displayCount);
	}

	private static double ComputeTileDataFraction(TiffDirectory dir, int tileIndex)
	{
		Type parsedType = dir.Metadata.ParsedType;
		try
		{
			dir.GetValidRegion(tileIndex, out var validWidth, out var validHeight);
			if (parsedType == typeof(float))
			{
				return DataFraction<float>(dir, tileIndex, validWidth, validHeight);
			}
			if (parsedType == typeof(double))
			{
				return DataFraction<double>(dir, tileIndex, validWidth, validHeight);
			}
			if (parsedType == typeof(int))
			{
				return DataFraction<int>(dir, tileIndex, validWidth, validHeight);
			}
			if (parsedType == typeof(uint))
			{
				return DataFraction<uint>(dir, tileIndex, validWidth, validHeight);
			}
			if (parsedType == typeof(short))
			{
				return DataFraction<short>(dir, tileIndex, validWidth, validHeight);
			}
			if (parsedType == typeof(ushort))
			{
				return DataFraction<ushort>(dir, tileIndex, validWidth, validHeight);
			}
			if (parsedType == typeof(byte))
			{
				return DataFraction<byte>(dir, tileIndex, validWidth, validHeight);
			}
			if (parsedType == typeof(sbyte))
			{
				return DataFraction<sbyte>(dir, tileIndex, validWidth, validHeight);
			}
			if (parsedType == typeof(long))
			{
				return DataFraction<long>(dir, tileIndex, validWidth, validHeight);
			}
			if (parsedType == typeof(ulong))
			{
				return DataFraction<ulong>(dir, tileIndex, validWidth, validHeight);
			}
			return 1.0;
		}
		catch
		{
			return -1.0;
		}
	}

	private static double DataFraction<T>(TiffDirectory dir, int tileIndex, int validWidth, int validHeight) where T : unmanaged, IEquatable<T>
	{
		long validWidthTimesValidHeight = (long)validWidth * (long)validHeight;
		if (validWidthTimesValidHeight <= 0)
		{
			return 0.0;
		}
		T[] tile = dir.GetTile<T>(tileIndex);
		T? noDataAs = dir.Metadata.NoDataAs<T>();
		if (!noDataAs.HasValue)
		{
			return 1.0;
		}
		T noDataAsTerm = noDataAs.Value;
		int tileWidth = dir.Metadata.TileWidth;
		long ordinal = 0L;
		for (int i = 0; i < validHeight; i++)
		{
			int tileWidthTerm = i * tileWidth;
			for (int j = 0; j < validWidth; j++)
			{
				if (!tile[tileWidthTerm + j].Equals(noDataAsTerm))
				{
					ordinal++;
				}
			}
		}
		return (double)ordinal / (double)validWidthTimesValidHeight;
	}

	public bool TryOpenSubdataset(SubdatasetEntry entry, out IRasterMetadataSource sub, out string error)
	{
		sub = null;
		error = "Native TIFF files have no sub-datasets.";
		return false;
	}

	private void Reopen()
	{
		if (TiffFile.TryLoad(_filename, out var file, out var _, out var _).Result && file != null && file.Directories.Count > 0)
		{
			_tiff = file;
		}
		else
		{
			file?.Dispose();
		}
	}

	private static IEnumerable<KeyValuePair<string, string>> ParseGdalMetadataItems(string xml)
	{
		if (string.IsNullOrWhiteSpace(xml))
		{
			yield break;
		}
		XDocument xDocument;
		try
		{
			xDocument = XDocument.Parse(xml);
		}
		catch
		{
			yield break;
		}
		foreach (XElement descendant in xDocument.Descendants("Item"))
		{
			string attributeDescendantTerm = descendant.Attribute("name")?.Value;
			if (!string.IsNullOrEmpty(attributeDescendantTerm))
			{
				yield return new KeyValuePair<string, string>(attributeDescendantTerm, descendant.Value);
			}
		}
	}

	private static string BuildGdalMetadataXml(string existingXml, string key, string textValue)
	{
		XDocument xDocument = ((!string.IsNullOrWhiteSpace(existingXml)) ? XDocument.Parse(existingXml) : new XDocument(new XElement("GDALMetadata")));
		XElement root = xDocument.Root;
		XElement xElement = root.Descendants("Item").FirstOrDefault((XElement e) => e.Attribute("name")?.Value == key);
		if (xElement != null)
		{
			xElement.Value = textValue;
		}
		else
		{
			root.Add(new XElement("Item", new XAttribute("name", key), textValue));
		}
		return xDocument.ToString(SaveOptions.DisableFormatting);
	}

	public void Dispose()
	{
		_tiff?.Dispose();
	}
}
