using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.ORM;
using Utility.Progress;

namespace Utility.Files;

[DebuggerDisplay("{Name,nq}: {RashXX3,nq}")]
public class FileSignature
{
	public static class MessageCodes
	{
		public static readonly MessageClass NotLoaded = new MessageClass("FileSig_NotLoaded", MessageSeverity.Error, "File Signature not loaded.");

		public static Message GetNotLoaded()
		{
			return NotLoaded.Create();
		}
	}

	[Metadata("Filename")]
	public string Name { get; set; }

	[Metadata("File Size")]
	public long FileSize { get; set; }

	[Metadata("Last Write Time UTC")]
	public DateTime DateLastModifiedUTC { get; set; }

	[Metadata("SHA256")]
	public string SHA256 { get; set; }

	[Metadata("RashXX3")]
	public string RashXX3 { get; set; }

	public bool IsLoaded()
	{
		if (FileSize > 0 && DateLastModifiedUTC != DateTime.MinValue)
		{
			return !string.IsNullOrEmpty(RashXX3);
		}
		return false;
	}

	public static FileSignature LoadFull(string filename)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		FileInfo fileInfo = new FileInfo(filename);
		return new FileSignature
		{
			DateLastModifiedUTC = StripKind(fileInfo.LastWriteTimeUtc),
			Name = Path.GetFileName(filename),
			FileSize = fileInfo.Length,
			SHA256 = FileHelpers.ComputeFileSHA256(filename),
			RashXX3 = FileHelpers.ComputeRashXX3(filename)
		};
	}

	public static FileSignature LoadDefault(string filename)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		FileInfo fileInfo = new FileInfo(filename);
		return new FileSignature
		{
			DateLastModifiedUTC = StripKind(fileInfo.LastWriteTimeUtc),
			Name = Path.GetFileName(filename),
			FileSize = fileInfo.Length,
			SHA256 = "",
			RashXX3 = FileHelpers.ComputeRashXX3(filename)
		};
	}

	public static FileSignature LoadFast(string filename)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		FileInfo fileInfo = new FileInfo(filename);
		return new FileSignature
		{
			DateLastModifiedUTC = StripKind(fileInfo.LastWriteTimeUtc),
			Name = Path.GetFileName(filename),
			FileSize = fileInfo.Length,
			SHA256 = "",
			RashXX3 = ""
		};
	}

	private static DateTime StripKind(DateTime dt)
	{
		return new DateTime(dt.Ticks);
	}

	public OperationResult Compare(FileSignature otherFile, FileCompareOptions opts = FileCompareOptions.SizeAndTimestamp, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (otherFile == null)
		{
			throw new ArgumentNullException("otherFile");
		}
		if (!IsLoaded() || !otherFile.IsLoaded())
		{
			return OperationResult.Fail(MessageCodes.GetNotLoaded());
		}
		return opts switch
		{
			FileCompareOptions.SizeAndTimestamp => CompareBySizeAndTimestampInternal(otherFile, pr), 
			FileCompareOptions.Hash => CompareByHash(otherFile, pr), 
			_ => throw new ArgumentException(opts.ToString() + " not recognized."), 
		};
	}

	public OperationResult Compare(string filename, FileCompareOptions opts = FileCompareOptions.SizeAndTimestamp, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		if (!IsLoaded())
		{
			return OperationResult.Fail(MessageCodes.GetNotLoaded());
		}
		return opts switch
		{
			FileCompareOptions.SizeAndTimestamp => CompareBySizeAndTimestampInternal(filename, pr), 
			FileCompareOptions.Hash => CompareByHash(filename, pr), 
			_ => throw new ArgumentException(opts.ToString() + " not recognized."), 
		};
	}

	private OperationResult CompareBySizeAndTimestampInternal(FileSignature otherFile, ProgressReporter pr)
	{
		if (FileSize != otherFile.FileSize)
		{
			return OperationResult.Fail("File size doesn't match.");
		}
		if (DateLastModifiedUTC != otherFile.DateLastModifiedUTC)
		{
			return OperationResult.Fail("File Last Write Time doesn't match.");
		}
		pr.ReportMessage("File signature matches (Size={Size}, Timestamp={Timestamp}).", LogLevel.Debug, FileSize, DateLastModifiedUTC);
		return true;
	}

	private OperationResult CompareBySizeAndTimestampInternal(string filename, ProgressReporter pr)
	{
		FileInfo fileInfo = new FileInfo(filename);
		if (FileSize != fileInfo.Length)
		{
			return OperationResult.Fail(FileMessageCodes.GetFileSizeDoesntMatch(Name, FileSize, fileInfo.Length));
		}
		if (DateLastModifiedUTC != fileInfo.LastWriteTimeUtc)
		{
			return OperationResult.Fail(FileMessageCodes.GetFileTimestampDoesntMatch(Name, DateLastModifiedUTC, fileInfo.LastWriteTimeUtc));
		}
		pr.ReportMessage("File signature matches (Size={Size}, Timestamp={Timestamp}).", LogLevel.Debug, FileSize, DateLastModifiedUTC);
		return true;
	}

	private OperationResult CompareByHash(FileSignature otherFile, ProgressReporter pr)
	{
		if (FileSize != otherFile.FileSize)
		{
			return OperationResult.Fail(FileMessageCodes.GetFileSizeDoesntMatch(Name, FileSize, otherFile.FileSize));
		}
		if (RashXX3 != otherFile.RashXX3)
		{
			return OperationResult.Fail(FileMessageCodes.GetFileHashDoesntMatch(Name, RashXX3, otherFile.RashXX3));
		}
		pr.ReportMessage("File hash matches (Size={Size}, Hash={Hash}).", LogLevel.Debug, FileSize, RashXX3);
		return true;
	}

	private OperationResult CompareByHash(string filename, ProgressReporter pr)
	{
		FileInfo fileInfo = new FileInfo(filename);
		if (FileSize != fileInfo.Length)
		{
			return OperationResult.Fail(FileMessageCodes.GetFileSizeDoesntMatch(Name, FileSize, fileInfo.Length));
		}
		string rashXX3 = FileHelpers.ComputeRashXX3(filename);
		if (RashXX3 != rashXX3)
		{
			return OperationResult.Fail(FileMessageCodes.GetFileHashDoesntMatch(Name, RashXX3, rashXX3));
		}
		pr.ReportMessage("File hash matches (Size={Size}, Hash={Hash}).", LogLevel.Debug, FileSize, RashXX3);
		return true;
	}
}
