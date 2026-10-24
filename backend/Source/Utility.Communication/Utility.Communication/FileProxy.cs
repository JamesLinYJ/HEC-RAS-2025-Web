using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using Utility.Files;
using Utility.Units;

namespace Utility.Communication;

public class FileProxy
{
	public string Filename { get; set; }

	public string ShortFilename { get; set; }

	public bool Exists { get; set; }

	public DateTime Timestamp { get; set; }

	public long FileSize { get; set; }

	public string FriendlyFileSize => FileHelpers.PrettyByteCount(FileSize);

	public string Hash { get; set; }

	public string HashFriendlyName
	{
		get
		{
			if (string.IsNullOrEmpty(Hash))
			{
				return string.Empty;
			}
			if (Hash.Length <= 6)
			{
				return Hash;
			}
			return Hash.Substring(Hash.Length - 6, 6);
		}
	}

	public string Owner { get; set; }

	public string OwnerFriendlyName
	{
		get
		{
			if (string.IsNullOrEmpty(Owner))
			{
				return string.Empty;
			}
			if (S3TransferHelper.FriendlyOwnerNames.TryGetValue(Owner, out var localValue))
			{
				return localValue;
			}
			return Owner;
		}
	}

	public bool IsCompressed { get; set; }

	public long CompressedFileSize { get; set; }

	public string CompressionRatioFriendlyName
	{
		get
		{
			if (CompressedFileSize == 0L || CompressedFileSize == FileSize)
			{
				return "N/A";
			}
			return Math.Round(100.0 * (double)CompressedFileSize / (double)FileSize) + "%";
		}
	}

	public double CompressionPercentage
	{
		get
		{
			if (CompressedFileSize == 0L || CompressedFileSize == FileSize)
			{
				return 100.0;
			}
			return 100.0 * (double)CompressedFileSize / (double)FileSize;
		}
	}

	public FileProxy()
	{
	}

	public FileProxy(string fn, bool exists, string hash, DateTime dt, string owner, long size)
	{
		Filename = fn;
		Exists = exists;
		Hash = hash;
		Timestamp = dt;
		Owner = owner;
		FileSize = size;
		ShortFilename = Filename;
	}

	public static FileProxy LocalFile(string fn)
	{
		if (File.Exists(fn))
		{
			FileInfo fileInfo = new FileInfo(fn);
			return new FileProxy(fn, exists: true, FileHelpers.ComputeFileMD5(fn), fileInfo.LastWriteTime, string.Empty, fileInfo.Length);
		}
		return DoesNotExist(fn);
	}

	public static async Task<FileProxy> LocalFileAsync(string fn)
	{
		if (File.Exists(fn))
		{
			FileInfo fi = new FileInfo(fn);
			return new FileProxy(fn, exists: true, await FileHelpers.ComputeFileMD5Async(fn), fi.LastWriteTime, string.Empty, fi.Length);
		}
		return DoesNotExist(fn);
	}

	public static async Task<FileProxy> LocalFileAsync(FileInfo fi)
	{
		string fn = fi.FullName;
		return new FileProxy(fn, exists: true, await FileHelpers.ComputeFileMD5Async(fn), fi.LastWriteTime, string.Empty, fi.Length);
	}

	public static FileProxy DoesNotExist(string filename)
	{
		return new FileProxy(filename, exists: false, string.Empty, DateTime.MinValue, string.Empty, 0L);
	}

	public XElement ToXElement()
	{
		XElement xElement = new XElement("FileProxy");
		xElement.Add("Filename", Filename);
		xElement.Add("Exists", Exists);
		xElement.Add("Hash", Hash);
		xElement.Add("LastModified", Time.ConvertToUnixTime(Timestamp));
		xElement.Add("Owner", Owner);
		xElement.Add("FileSize", FileSize);
		xElement.Add("IsCompressed", IsCompressed);
		xElement.Add("CompressedSize", CompressedFileSize);
		return xElement;
	}

	public static FileProxy TryLoadXML(XElement el)
	{
		XElement xElement = el.Element("Filename");
		if (xElement == null)
		{
			return null;
		}
		string xElementTerm = xElement.Value;
		xElement = el.Element("Exists");
		if (xElement == null)
		{
			return null;
		}
		if (!bool.TryParse(xElement.Value, out var result))
		{
			return null;
		}
		xElement = el.Element("Hash");
		if (xElement == null)
		{
			return null;
		}
		string xElementTerm2 = xElement.Value;
		xElement = el.Element("LastModified");
		if (xElement == null)
		{
			return null;
		}
		if (!long.TryParse(xElement.Value, out var result2))
		{
			return null;
		}
		DateTime dt = Time.ConvertToLocalTime(result2);
		xElement = el.Element("Owner");
		if (xElement == null)
		{
			return null;
		}
		string xElementTerm3 = xElement.Value;
		xElement = el.Element("FileSize");
		if (xElement == null)
		{
			return null;
		}
		if (!long.TryParse(xElement.Value, out var result3))
		{
			return null;
		}
		xElement = el.Element("IsCompressed");
		if (xElement == null)
		{
			return null;
		}
		if (!bool.TryParse(xElement.Value, out var result4))
		{
			return null;
		}
		xElement = el.Element("CompressedSize");
		if (xElement == null)
		{
			return null;
		}
		if (!long.TryParse(xElement.Value, out var result5))
		{
			return null;
		}
		return new FileProxy(xElementTerm, result, xElementTerm2, dt, xElementTerm3, result3)
		{
			IsCompressed = result4,
			CompressedFileSize = result5
		};
	}
}
