using System;
using System.IO;
using System.Text;

namespace TiffBinaryReader;

public static class TiffMetadataEditor
{
	public static bool TrySetAscii(string filename, long directoryOffset, bool isBigTiff, TagType tag, string stringValue, out string error)
	{
		byte[] bytes = Encoding.ASCII.GetBytes(stringValue ?? "");
		return TryOverwriteTag(filename, directoryOffset, isBigTiff, tag, DataTypes.Ascii, bytes, bytes.Length, out error);
	}

	public static bool TrySetDoubles(string filename, long directoryOffset, bool isBigTiff, TagType tag, ReadOnlySpan<double> values, out string error)
	{
		byte[] bytes = new byte[values.Length * 8];
		for (int i = 0; i < values.Length; i++)
		{
			BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 8);
		}
		return TryOverwriteTag(filename, directoryOffset, isBigTiff, tag, DataTypes.Double, bytes, values.Length, out error);
	}

	public static bool TryOverwriteTag(string filename, long directoryOffset, bool isBigTiff, TagType tag, DataType expectedType, byte[] newBytes, int newValueCount, out string error)
	{
		error = null;
		try
		{
			using FileStream fileStream = new FileStream(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			using BinaryReader binaryReader = new BinaryReader(fileStream, Encoding.ASCII, leaveOpen: true);
			using BinaryWriter binaryWriter = new BinaryWriter(fileStream, Encoding.ASCII, leaveOpen: true);
			int isBigTiffTerm = (isBigTiff ? 20 : 12);
			int isBigTiffTerm2 = (isBigTiff ? 8 : 4);
			int isBigTiffTerm3 = (isBigTiff ? 12 : 8);
			fileStream.Position = directoryOffset;
			long uInt64 = (long)(isBigTiff ? binaryReader.ReadUInt64() : binaryReader.ReadUInt16());
			long position = fileStream.Position;
			for (long ordinal = 0L; ordinal < uInt64; ordinal++)
			{
				long positionFileStreamTerm = (fileStream.Position = position + ordinal * isBigTiffTerm);
				ushort uInt16 = binaryReader.ReadUInt16();
				ushort typeID = binaryReader.ReadUInt16();
				long uInt642 = (long)(isBigTiff ? binaryReader.ReadUInt64() : binaryReader.ReadUInt32());
				if (uInt16 != tag.ID)
				{
					continue;
				}
				DataType dataType = DataTypes.Get(typeID);
				if (dataType == null)
				{
					error = "Tag '" + tag.Name + "' has an unrecognized on-disk data type.";
					return false;
				}
				if (dataType.UID != expectedType.UID)
				{
					error = $"Tag '{tag.Name}' is stored as {dataType.Description}, expected {expectedType.Description}; cannot edit in place.";
					return false;
				}
				long uInt642TimesByteCount = uInt642 * dataType.ByteCount;
				bool condition = uInt642TimesByteCount <= isBigTiffTerm2;
				if (condition && newBytes.Length > isBigTiffTerm2)
				{
					error = $"New value for '{tag.Name}' ({newBytes.Length} bytes) is too large to edit in place (inline limit {isBigTiffTerm2} bytes).";
					return false;
				}
				if (!condition && newBytes.Length > uInt642TimesByteCount)
				{
					error = $"New value for '{tag.Name}' ({newBytes.Length} bytes) exceeds the existing {uInt642TimesByteCount} bytes; cannot grow in place.";
					return false;
				}
				if (newValueCount != uInt642)
				{
					fileStream.Position = positionFileStreamTerm + 4;
					if (isBigTiff)
					{
						binaryWriter.Write((ulong)newValueCount);
					}
					else
					{
						binaryWriter.Write((uint)newValueCount);
					}
				}
				if (newBytes.Length <= isBigTiffTerm2)
				{
					Span<byte> newBytesSpan = stackalloc byte[isBigTiffTerm2];
					newBytesSpan.Clear();
					newBytes.AsSpan().CopyTo(newBytesSpan);
					fileStream.Position = positionFileStreamTerm + isBigTiffTerm3;
					binaryWriter.Write(newBytesSpan);
				}
				else
				{
					fileStream.Position = positionFileStreamTerm + isBigTiffTerm3;
					long position2 = (long)(isBigTiff ? binaryReader.ReadUInt64() : binaryReader.ReadUInt32());
					fileStream.Position = position2;
					binaryWriter.Write(newBytes);
				}
				binaryWriter.Flush();
				return true;
			}
			error = "Tag '" + tag.Name + "' is not present in this directory.";
			return false;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}
}
