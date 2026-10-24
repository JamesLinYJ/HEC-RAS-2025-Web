using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TiffBinaryReader;

public static class TagTypes
{
	private const string NO_DESC = "No description available.";

	private static List<TagType> _tagList = new List<TagType>();

	public static ReadOnlyCollection<TagType> TagList = new ReadOnlyCollection<TagType>(_tagList);

	public static TagType ImageWidth = TagType.CreateNew("ImageWidth", TagValues.IMAGEWIDTH, DataTypes.AllIntegralTypes, "Width of the raster, in pixels", 1uL);

	public static TagType ImageLength = TagType.CreateNew("ImageLength", TagValues.IMAGELENGTH, DataTypes.AllIntegralTypes, "Height of the raster, in pixels", 1uL);

	public static TagType BitsPerSample = TagType.CreateNew("BitsPerSample", TagValues.BITSPERSAMPLE, DataTypes.AllUnsignedIntegralTypes, "Number of bits (Normally 8/16/32/64) to represent each value. One per band.", 64uL);

	public static TagType SubfileType = TagType.CreateNew("SubfileType", TagValues.SUBFILETYPE, DataTypes.AllUnsignedIntegralTypes, "Indicates the type of data in this page - overview, mask, etc.", 1uL);

	public static TagType Compression = TagType.CreateNew("Compression", TagValues.COMPRESSION, DataTypes.AllUnsignedIntegralTypes, "Defines the compression type used for this page.", 1uL);

	public static TagType PhotometricInterpID = TagType.CreateNew("PhotometricInterpID", TagValues.PHOTOMETRIC, DataTypes.AllUnsignedIntegralTypes, "Photometric/Visual interpretation of the data.", 1uL);

	public static TagType FillOrder = TagType.CreateNew("FillOrder", TagValues.FILLORDER, DataTypes.AllUnsignedIntegralTypes, "Defines how the data is stored within a byte: MSB first or LSB first.", 1uL);

	public static TagType Orientation = TagType.CreateNew("Orientation", TagValues.ORIENTATION, DataTypes.AllUnsignedIntegralTypes, "Defines how the rows and columns are to be interpreted spatially, e.g. is the 0-row down or up?", 1uL);

	public static TagType SamplesPerPixel = TagType.CreateNew("SamplesPerPixel", TagValues.SAMPLESPERPIXEL, DataTypes.AllUnsignedIntegralTypes, "How many channels of data does the raster have? (Elevs = 1, RGB Image = 3, etc.)", 1uL);

	public static TagType PlanarConfig = TagType.CreateNew("PlanarConfig", TagValues.PLANARCONFIG, DataTypes.AllUnsignedIntegralTypes, "How is multi-channel data handled? RGBRGBRGB vs RRRGGGBBB", 1uL);

	public static TagType SampleFormat = TagType.CreateNew("SampleFormat", TagValues.SAMPLEFORMAT, DataTypes.AllUnsignedIntegralTypes, "How should we interpret each data sample? Int, Float, etc. One per band.", 64uL);

	public static TagType ExtraSamples = TagType.CreateNew("ExtraSamples", TagValues.EXTRASAMPLES, DataTypes.AllShortTypes, "Describes samples beyond the baseline photometric interpretation (e.g. alpha channel for RGB).", ulong.MaxValue);

	public static TagType Predictor = TagType.CreateNew("Predictor", TagValues.PREDICTOR, DataTypes.AllUnsignedIntegralTypes, "Compression predictor hints.", 1uL);

	public static TagType XResolution = TagType.CreateNew("XResolution", TagValues.XRESOLUTION, DataTypes.AllRationalTypes, "The number of pixels per ResolutionUnit in the ImageWidth direction.", 1uL);

	public static TagType YResolution = TagType.CreateNew("YResolution", TagValues.YRESOLUTION, DataTypes.AllRationalTypes, "The number of pixels per ResolutionUnit in the ImageLength direction.", 1uL);

	public static TagType ResolutionUnit = TagType.CreateNew("ResolutionUnit", TagValues.RESOLUTIONUNIT, DataTypes.AllShortTypes, "The unit of measurement for XResolution and YResolution. 1 = No absolute unit of measurement. Used for images that may have a non-square aspect ratio, but no meaningful absolute dimensions, 2 = Inch, 3 = Centimeter.", 1uL);

	public static TagType TileWidth = TagType.CreateNew("TileWidth", TagValues.TILEWIDTH, DataTypes.AllIntegralTypes, "Width of each tile, in pixels", 1uL);

	public static TagType TileLength = TagType.CreateNew("TileLength", TagValues.TILELENGTH, DataTypes.AllIntegralTypes, "Height of each tile, in pixels", 1uL);

	public static TagType TileByteOffsets = TagType.CreateNew("TileByteOffsets", TagValues.TILEOFFSETS, DataTypes.AllIntegralTypes, "Byte offsets to each data tile on disk.", ulong.MaxValue);

	public static TagType TileByteCounts = TagType.CreateNew("TileByteCounts", TagValues.TILEBYTECOUNTS, DataTypes.AllUnsignedIntegralTypes, "Byte lengths for each (potentially compressed) data tile on disk.", ulong.MaxValue);

	public static TagType StripOffsets = TagType.CreateNew("StripOffsets", TagValues.STRIPOFFSETS, DataTypes.AllIntegralTypes, "Byte offsets to each data strip on disk.", ulong.MaxValue);

	public static TagType StripByteCounts = TagType.CreateNew("StripByteCounts", TagValues.STRIPBYTECOUNTS, DataTypes.AllUnsignedIntegralTypes, "Byte lengths for each strip on disk.", ulong.MaxValue);

	public static TagType RowsPerStrip = TagType.CreateNew("RowsPerStrip", TagValues.ROWSPERSTRIP, DataTypes.AllUnsignedIntegralTypes, "Number of rows per strip.", 1uL);

	public static TagType ModelPixelScale = TagType.CreateNew("ModelPixelScale", TagValues.GEOTIFF_MODELPIXELSCALETAG, DataTypes.AllFloatingTypes, "Geotiff tag for affine transformations. (ScaleX, ScaleY, ScaleZ)", 3uL);

	public static TagType ModelTiePoint = TagType.CreateNew("ModelTiePoint", TagValues.GEOTIFF_MODELTIEPOINTTAG, DataTypes.AllFloatingTypes, "Geotiff tag for raster->model tiepoint pairs", ulong.MaxValue);

	public static TagType ModelTransformation = TagType.CreateNew("ModelTransformation", TagValues.GEOTIFF_MODELTRANSFORMATIONTAG, DataTypes.AllFloatingTypes, "Geotiff tag (optional) for further defining affine transformations.", 16uL);

	public static TagType GeoKeyDirectory = TagType.CreateNew("GeoKeyDirectory", TagValues.GEOTIFF_GEOKEYDIRECTORYTAG, DataTypes.AllUnsignedIntegralTypes, "Geotiff tag that helps store projection information.", ulong.MaxValue);

	public static TagType GeoDoubleParams = TagType.CreateNew("GeoDoubleParams", TagValues.GEOTIFF_GEODOUBLEPARAMSTAG, DataTypes.Double, "Geotiff tag that helps store all of the DOUBLE valued GeoKeys, referenced by the GeoKeyDirectoryTag.", ulong.MaxValue);

	public static TagType GeoAsciiParams = TagType.CreateNew("GeoAsciiParams", TagValues.GEOTIFF_GEOASCIIPARAMSTAG, DataTypes.Ascii, "Geotiff tag that helps store projection information.", ulong.MaxValue);

	public static TagType GDALMetadata = TagType.CreateNew("GDALMetadata", TagValues.GEOTIFF_GDALMETADATATAG, DataTypes.Ascii, "GDAL Tag for stats and other metadata", ulong.MaxValue);

	public static TagType GDALNoData = TagType.CreateNew("GDALNoData", TagValues.GEOTIFF_GDALNODATATAG, DataTypes.Ascii, "GDAL Tag storing no-data (redundant).", ulong.MaxValue);

	public static TagType DocumentName = TagType.CreateNew("DocumentName", TagValues.DOCUMENTNAME, DataTypes.Ascii, "The name of the document from which this image was scanned.", ulong.MaxValue);

	public static TagType ImageDescription = TagType.CreateNew("ImageDescription", TagValues.IMAGEDESCRIPTION, DataTypes.Ascii, "Text description of the tiff file contents.", ulong.MaxValue);

	public static TagType Artist = TagType.CreateNew("Artist", TagValues.ARTIST, DataTypes.Ascii, "Text description of the person/company/software that created this file.", ulong.MaxValue);

	public static TagType Software = TagType.CreateNew("Software", TagValues.SOFTWARE, DataTypes.Ascii, "Name and version number of the software package(s) used to create the image.", ulong.MaxValue);

	public static TagType ColorMap = TagType.CreateNew("ColorMap", TagValues.COLORMAP, DataTypes.AllShortTypes, "A color map for palette color images.", ulong.MaxValue);

	public static void Register(TagType t)
	{
		if (_tagList.FirstOrDefault((TagType tt) => tt.ID == t.ID) == null)
		{
			_tagList.Add(t);
			return;
		}
		throw new Exception("Tag has already been registered.");
	}

	public static string GetSubfileTypeDescription(ushort id)
	{
		if ((id & 1) == 1)
		{
			return "Reduced Image";
		}
		if ((id & 2) == 2)
		{
			return "Page";
		}
		if ((id & 4) == 4)
		{
			return "Mask";
		}
		return "Unknown";
	}

	public static string GetFillOrderDescription(ushort id)
	{
		return id switch
		{
			1 => "MSB First", 
			2 => "LSB First", 
			_ => "Unknown", 
		};
	}

	public static string GetPhotometricDescription(ushort id)
	{
		return id switch
		{
			0 => "WhiteIsZero", 
			1 => "BlackIsZero", 
			2 => "RGB", 
			3 => "RGB Palette", 
			4 => "Transparency Mask", 
			5 => "CMYK", 
			6 => "YCbCr", 
			8 => "CIELab", 
			_ => "Unknown", 
		};
	}

	public static string GetCompressionDescription(ushort id)
	{
		return id switch
		{
			1 => "Uncompressed", 
			2 => "CCITT 1D", 
			3 => "Group 3 Fax", 
			4 => "Group 4 Fax", 
			5 => "LZW", 
			6 => "Old JPEG", 
			7 => "New JPEG", 
			8 => "Deflate", 
			32773 => "PackBits", 
			_ => "Unknown", 
		};
	}
}
