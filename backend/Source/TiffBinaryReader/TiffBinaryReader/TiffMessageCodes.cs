using Utility.Logging;

namespace TiffBinaryReader;

public static class TiffMessageCodes
{
	public static readonly MessageClass NoDirectoriesParsed = new MessageClass("TIFF_NO_DIRS", MessageSeverity.Error, "No directories could be parsed from TIFF file.");

	public static readonly MessageClass BigEndianUnsupported = new MessageClass("TIFF_BIG_ENDIAN", MessageSeverity.Error, "Big Endian TIFF files are not supported.");

	public static readonly MessageClass InvalidMagicNumber = new MessageClass("TIFF_INVALID_MAGIC", MessageSeverity.Error, "File magic number not recognized.");

	public static readonly MessageClass InvalidTiffIdentifier = new MessageClass("TIFF_INVALID_ID", MessageSeverity.Error, "File is not a well-formatted tiff: The magic number does not correspond to either a regular TIFF or BigTIFF.");

	public static readonly MessageClass OvrLoadFailed = new MessageClass("TIFF_OVR_FAILED", MessageSeverity.Warning, "Failed to load OVR file: {Message}");

	public static readonly MessageClass TileExceedsFileLength = new MessageClass("TIFF_TILE_TRUNCATED", MessageSeverity.Error, "Tile {TileIndex} data extends beyond end of file (offset {Offset} + length {Length} > file length {FileLength}). File may be truncated or corrupted.");
}
