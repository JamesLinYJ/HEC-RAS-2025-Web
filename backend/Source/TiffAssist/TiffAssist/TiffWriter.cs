using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using Utility;

namespace TiffAssist;

public abstract class TiffWriter<T> : TiffWriterBase where T : struct
{
	protected int CurrentDir { get; private set; }

	protected int Width { get; private set; }

	protected int Height { get; private set; }

	protected int TilesWide { get; private set; }

	protected int TilesTall { get; private set; }

	protected int SizeOfT { get; private set; }

	protected bool TrackStats { get; private set; }

	protected TiffMetadata<T> Metadata { get; private set; }

	public Dictionary<int, Stats> TileStats { get; private set; }

	public TiffWriter(string tiffFilename, int imageWidth, int imageHeight, SampleFormat dataFormat, bool trackStats, TiffMetadata<T> metadata, string appendArg = "", bool deleteExistingFile = true)
		: base(tiffFilename, (File.Exists(tiffFilename) && !deleteExistingFile) ? ("a" + appendArg) : ("w" + appendArg), deleteExistingFile)
	{
		Initialize(imageWidth, imageHeight, dataFormat, trackStats, metadata);
	}

	public TiffWriter(Stream stream, int imageWidth, int imageHeight, SampleFormat dataFormat, bool trackStats, TiffMetadata<T> metadata)
		: base(stream)
	{
		Initialize(imageWidth, imageHeight, dataFormat, trackStats, metadata);
	}

	private void Initialize(int imageWidth, int imageHeight, SampleFormat dataFormat, bool trackStats, TiffMetadata<T> metadata)
	{
		SizeOfT = Marshal.SizeOf<T>(default);
		Width = imageWidth;
		Height = imageHeight;
		Metadata = metadata;
		TrackStats = trackStats;
		TileStats = new Dictionary<int, Stats>();
		if (!string.IsNullOrEmpty(Metadata.Artist))
		{
			_tiffImage.SetField(TiffTag.ARTIST, Metadata.Artist);
		}
		if (!string.IsNullOrEmpty(Metadata.Software))
		{
			_tiffImage.SetField(TiffTag.SOFTWARE, Metadata.Software);
		}
		if (!string.IsNullOrEmpty(Metadata.ImageDescription))
		{
			_tiffImage.SetField(TiffTag.IMAGEDESCRIPTION, Metadata.ImageDescription);
		}
		_tiffImage.SetField(TiffTag.IMAGEWIDTH, Width);
		_tiffImage.SetField(TiffTag.IMAGELENGTH, Height);
		_tiffImage.SetField(TiffTag.TILEWIDTH, Metadata.TileWidth);
		_tiffImage.SetField(TiffTag.TILELENGTH, Metadata.TileHeight);
		_tiffImage.SetField(TiffTag.SAMPLESPERPIXEL, 1);
		_tiffImage.SetField(TiffTag.BITSPERSAMPLE, SizeOfT * 8);
		_tiffImage.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
		_tiffImage.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
		_tiffImage.SetField(TiffTag.SAMPLEFORMAT, dataFormat);
		_tiffImage.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISBLACK);
		_tiffImage.SetField(TiffTag.COMPRESSION, Compression.ADOBE_DEFLATE);
		_tiffImage.SetField(TiffTag.ZIPQUALITY, 1);
		if (Metadata.IsLowResOverlay)
		{
			_tiffImage.SetField(TiffTag.SUBFILETYPE, FileType.REDUCEDIMAGE);
		}
		_tiffImage.SetField(TiffTag.FILLORDER, FillOrder.MSB2LSB);
		CurrentDir = GetNumDirectories();
		TilesWide = GetNumTilesWide(CurrentDir);
		TilesTall = GetNumTilesTall(CurrentDir);
	}

	public abstract void WriteTile(int tileNumber, T[] data);

	public abstract void WriteTile(int tileNumber, T[,] data);

	protected override void Dispose(bool isDisposing)
	{
		if (!_isDisposed)
		{
			if (string.IsNullOrEmpty(Metadata.NoDataValueStringOverride))
			{
				_tiffImage.SetField(TiffTag.GDAL_NODATA, Metadata.NoDataValue.ToString());
			}
			else
			{
				_tiffImage.SetField(TiffTag.GDAL_NODATA, Metadata.NoDataValueStringOverride);
			}
			base.Dispose(isDisposing);
		}
	}
}
