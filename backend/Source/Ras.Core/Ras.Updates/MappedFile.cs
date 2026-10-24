using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Ras.Updates;

[DebuggerDisplay("{Filename}")]
public class MappedFile
{
	public string SHA256 { get; set; }

	public string Filename { get; set; }

	public long Length { get; set; }

	public DateTime LastModified { get; set; }

	public static MappedFile ForFile(string filename, string relDir = null)
	{
		string filename2 = (string.IsNullOrEmpty(relDir) ? filename : Path.GetRelativePath(relDir, filename));
		using FileStream fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read);
		string sHA = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileStream));
		DateTime lastWriteTime = new FileInfo(filename).LastWriteTime;
		return new MappedFile
		{
			Filename = filename2,
			Length = fileStream.Length,
			SHA256 = sHA,
			LastModified = lastWriteTime
		};
	}
}
