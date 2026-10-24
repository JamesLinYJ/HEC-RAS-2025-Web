using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;

namespace TiffBinaryReader;

[DebuggerDisplay("{System.IO.Path.GetFileName(Filename)}")]
public class TiffFile : IDisposable
{
	public const ushort REGTIFF_ID = 42;

	public const ushort BIGTIFF_ID = 43;

	private static readonly ILogger _logger = LogHelper.GetLogger("TiffFile");

	private bool _isDisposed;

	private readonly List<TiffDirectory> _directories;

	private TiffReadConfigs _readConfigs;

	private bool _cleanFsPool;

	private Pool<Stream> _fsPool;

	private TiffFile _ovr;

	private readonly TiffFile _parent;

	private static readonly HashSet<int> DataPointerTagIds = new HashSet<int> { 324, 325, 273, 279 };

	public OperationResult LoadResult { get; private set; }

	public string Filename { get; private set; }

	public bool LittleEndian { get; private set; }

	public bool IsBigTiff { get; private set; }

	public IReadOnlyList<TiffDirectory> Directories => _directories;

	internal Pool<Stream> FSPool => _fsPool;

	public bool HasParentTiff => _parent != null;

	public TiffFile ParentTiff => _parent;

	public TiffFile(string filename, TiffReadConfigs? configs = null)
	{
		Filename = filename;
		_directories = new List<TiffDirectory>();
		_readConfigs = configs ?? TiffReadConfigs.DefaultConfigs;
		LoadResult = OperationResult.Success();
		_fsPool = new Pool<Stream>(() => new FileStream(Filename, FileMode.Open, FileAccess.Read, FileShare.Read));
		_cleanFsPool = true;
		Stopwatch stopwatch = Stopwatch.StartNew();
		using (PoolHandle<Stream> poolHandle = _fsPool.Acquire())
		{
			Init(poolHandle.Data);
		}
		if (_readConfigs.ParseOvrFile && _readConfigs.ParsePyramidLevels && !Path.GetExtension(filename).RoughlyEquals(".ovr"))
		{
			string filenameTerm = filename + ".ovr";
			if (File.Exists(filenameTerm))
			{
				_ovr = new TiffFile(this, filenameTerm);
				_directories.AddRange(_ovr.Directories);
			}
		}
		PostLoad();
		stopwatch.Stop();
		_logger?.LogInformation("Opened and scanned {Filename} in {Duration} ms.", Path.GetFileName(Filename), stopwatch.Elapsed.TotalMilliseconds);
	}

	private TiffFile(TiffFile root, string filename)
	{
		_parent = root;
		Filename = filename;
		_directories = new List<TiffDirectory>();
		_readConfigs = TiffReadConfigs.DefaultConfigs;
		LoadResult = OperationResult.Success();
		_fsPool = new Pool<Stream>(() => new FileStream(Filename, FileMode.Open, FileAccess.Read, FileShare.Read));
		_cleanFsPool = true;
		Stopwatch stopwatch = Stopwatch.StartNew();
		using (PoolHandle<Stream> poolHandle = _fsPool.Acquire())
		{
			Init(poolHandle.Data);
		}
		PostLoad();
		stopwatch.Stop();
		_logger?.LogInformation("Opened and scanned {Filename} in {Duration} ms.", Path.GetFileName(Filename), stopwatch.Elapsed.TotalMilliseconds);
	}

	public TiffFile(Pool<Stream> fileStreams, TiffReadConfigs? configs = null)
	{
		_directories = new List<TiffDirectory>();
		_readConfigs = configs ?? TiffReadConfigs.DefaultConfigs;
		LoadResult = OperationResult.Success();
		_fsPool = fileStreams;
		_cleanFsPool = false;
		using (PoolHandle<Stream> poolHandle = _fsPool.Acquire())
		{
			Init(poolHandle.Data);
		}
		PostLoad();
	}

	private TiffFile()
	{
		_directories = new List<TiffDirectory>();
		LoadResult = OperationResult.Success();
	}

	public static OperationResult TryLoad(string filename, out TiffFile file, out bool canReadBaseDirectory, out bool canReadAllDirectories)
	{
		file = null;
		try
		{
			if (!IsValidTiff(filename))
			{
				file = null;
				canReadBaseDirectory = false;
				canReadAllDirectories = false;
				return OperationResult.Fail("Not a valid/readable TIFF file.");
			}
			file = new TiffFile(filename);
			if (file.Directories.Count == 0)
			{
				canReadBaseDirectory = false;
				canReadAllDirectories = false;
				return true;
			}
			canReadBaseDirectory = file.Directories[0].CanReadData;
			canReadAllDirectories = file.Directories.All((TiffDirectory d) => d.CanReadData);
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error parsing TIFF file {Filename}.", filename.InDoubleQuotes());
			file?.Dispose();
			file = null;
			canReadBaseDirectory = false;
			canReadAllDirectories = false;
			return OperationResult.Fail("Error parsing TIFF file.", ex);
		}
	}

	private bool Init(Stream s)
	{
		s.Position = 0L;
		BinaryReader binaryReader = new BinaryReader(s);
		if (!ValidateHeader(binaryReader.ReadBytes(16), out var isLE, out var isBigTiff, out var errMsg))
		{
			_logger?.LogWarning("Error opening {Filename}: {ErrorMessage}", Filename, errMsg?.EvaluatedMessage ?? "Unknown error");
			if (errMsg.HasValue)
			{
				LoadResult.AddFailure(errMsg.Value);
			}
			return false;
		}
		LittleEndian = isLE == true;
		IsBigTiff = isBigTiff == true;
		_directories.AddRange(TiffDirectory.Parse(this, binaryReader, _readConfigs.ParsePyramidLevels));
		return true;
	}

	public static async Task<TiffFile> CreateAsync(string filename, TiffReadConfigs? configs = null, CancellationToken ct = default(CancellationToken))
	{
		TiffFile tiff = new TiffFile
		{
			Filename = filename,
			_readConfigs = (configs ?? TiffReadConfigs.DefaultConfigs),
			_fsPool = new Pool<Stream>(() => new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read)),
			_cleanFsPool = true
		};
		Stopwatch sw = Stopwatch.StartNew();
		using (PoolHandle<Stream> wrapper = tiff._fsPool.Acquire())
		{
			await tiff.InitAsync(wrapper.Data, ct);
		}
		if (tiff._readConfigs.ParseOvrFile && tiff._readConfigs.ParsePyramidLevels && !Path.GetExtension(filename).RoughlyEquals(".ovr"))
		{
			string filenameTerm = filename + ".ovr";
			if (File.Exists(filenameTerm))
			{
				TiffFile tiffFile = tiff;
				tiffFile._ovr = await CreateAsync(filenameTerm, TiffReadConfigs.DefaultConfigs, ct);
				tiff._directories.AddRange(tiff._ovr.Directories);
			}
		}
		tiff.PostLoad();
		sw.Stop();
		_logger?.LogInformation("Async opened {Filename} in {Duration} ms.", Path.GetFileName(filename), sw.Elapsed.TotalMilliseconds);
		return tiff;
	}

	public static async Task<TiffFile> CreateFromStreamPoolAsync(Pool<Stream> fileStreams, TiffReadConfigs? configs = null, CancellationToken ct = default(CancellationToken))
	{
		TiffFile tiff = new TiffFile
		{
			_readConfigs = (configs ?? TiffReadConfigs.DefaultConfigs),
			_fsPool = fileStreams,
			_cleanFsPool = false
		};
		using (PoolHandle<Stream> wrapper = tiff._fsPool.Acquire())
		{
			await tiff.InitAsync(wrapper.Data, ct);
		}
		tiff.PostLoad();
		return tiff;
	}

	private async Task<bool> InitAsync(Stream s, CancellationToken ct)
	{
		s.Position = 0L;
		byte[] startupBytes = new byte[16];
		await s.ReadExactlyAsync(startupBytes, ct);
		if (!ValidateHeader(startupBytes, out var isLE, out var isBigTiff, out var errMsg))
		{
			_logger?.LogWarning("Error opening {Filename}: {ErrorMessage}", Filename, errMsg?.EvaluatedMessage ?? "Unknown error");
			if (errMsg.HasValue)
			{
				LoadResult.AddFailure(errMsg.Value);
			}
			return false;
		}
		LittleEndian = isLE == true;
		IsBigTiff = isBigTiff == true;
		List<TiffDirectory> collection = await TiffDirectory.ParseAsync(this, s, _readConfigs.ParsePyramidLevels, ct);
		_directories.AddRange(collection);
		return true;
	}

	private static bool ValidateHeader(byte[] startup16, out bool? isLE, out bool? isBigTiff, out Message? errMsg)
	{
		if (startup16[0] == 73 && startup16[1] == 73)
		{
			isLE = true;
			switch (BitConverter.ToUInt16(startup16, 2))
			{
			case 42:
				isBigTiff = false;
				break;
			case 43:
				isBigTiff = true;
				break;
			default:
				isBigTiff = null;
				errMsg = TiffMessageCodes.InvalidTiffIdentifier.Create();
				return false;
			}
			errMsg = null;
			return true;
		}
		if (startup16[0] == 77 && startup16[1] == 77)
		{
			isBigTiff = null;
			isLE = false;
			errMsg = TiffMessageCodes.BigEndianUnsupported.Create();
			return false;
		}
		isBigTiff = null;
		isLE = null;
		errMsg = TiffMessageCodes.InvalidMagicNumber.Create();
		return false;
	}

	private static bool IsValidTiff(string filename)
	{
		if (!File.Exists(filename))
		{
			return false;
		}
		using FileStream fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (fileStream.Length < 16)
		{
			return false;
		}
		bool? isLE;
		bool? isBigTiff;
		Message? errMsg;
		return ValidateHeader(new BinaryReader(fileStream).ReadBytes(16), out isLE, out isBigTiff, out errMsg);
	}

	private void PostLoad()
	{
		foreach (TiffDirectory directory in _directories)
		{
			if (directory.LoadResult != null && !directory.LoadResult.Result)
			{
				LoadResult.MergeIn(directory.LoadResult);
			}
		}
		if (_ovr != null && !_ovr.LoadResult.Result)
		{
			LoadResult.AddMessage(TiffMessageCodes.OvrLoadFailed.Create(_ovr.LoadResult.GetConcatenatedMessages()));
		}
		if (_directories.Count == 0 && LoadResult.Result)
		{
			LoadResult.AddFailure(TiffMessageCodes.NoDirectoriesParsed.Create());
		}
	}

	public static void WriteMetadataOnly(TiffFile source, string destPath)
	{
		ArgumentNullException.ThrowIfNull(source, "source");
		if (source.Directories.Count == 0)
		{
			throw new ArgumentException("Source has no directories.", "source");
		}
		using FileStream fileStream = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write);
		Span<byte> byteSpan = stackalloc byte[16];
		byteSpan[0] = 73;
		byteSpan[1] = 73;
		byteSpan[2] = 43;
		byteSpan[3] = 0;
		byteSpan[4] = 8;
		byteSpan[5] = 0;
		fileStream.Write(byteSpan);
		long position3 = 0L;
		long writeDirectoryMetadataOnly = 0L;
		foreach (TiffDirectory directory in source.Directories)
		{
			long position = fileStream.Position;
			long writeDirectoryMetadataOnly2 = WriteDirectoryMetadataOnly(fileStream, directory.Metadata);
			if (position3 == 0L)
			{
				position3 = position;
			}
			if (writeDirectoryMetadataOnly != 0L)
			{
				long position2 = fileStream.Position;
				fileStream.Seek(writeDirectoryMetadataOnly, SeekOrigin.Begin);
				fileStream.Write(BitConverter.GetBytes(position));
				fileStream.Seek(position2, SeekOrigin.Begin);
			}
			writeDirectoryMetadataOnly = writeDirectoryMetadataOnly2;
		}
		fileStream.Seek(8L, SeekOrigin.Begin);
		fileStream.Write(BitConverter.GetBytes(position3));
	}

	private static long WriteDirectoryMetadataOnly(Stream fs, TagMetadata meta)
	{
		List<Tag> orderBy = meta.Tags.OrderBy((Tag t) => t.TagType.ID).ToList();
		int count = orderBy.Count;
		long positionFsTerm = fs.Position + 8 + count * 20 + 8;
		List<byte[]> byteValuesList = new List<byte[]>();
		byte[] byteValues = new byte[count * 20];
		int ordinal = 0;
		foreach (Tag orderBy2 in orderBy)
		{
			ushort iD = orderBy2.TagType.ID;
			ushort uID = orderBy2.ActualDataType.UID;
			int byteCount = orderBy2.ActualDataType.ByteCount;
			ulong valueCountOrderBy2Term;
			byte[] byteValuesArray;
			if (DataPointerTagIds.Contains(iD))
			{
				valueCountOrderBy2Term = (ulong)orderBy2.ValueCount;
				byteValuesArray = new byte[valueCountOrderBy2Term * 8];
				uID = DataTypes.ULong8.UID;
			}
			else
			{
				byteValuesArray = orderBy2.GetBytes().ToArray();
				valueCountOrderBy2Term = (ulong)(byteValuesArray.Length / byteCount);
			}
			Buffer.BlockCopy(BitConverter.GetBytes(iD), 0, byteValues, ordinal, 2);
			Buffer.BlockCopy(BitConverter.GetBytes(uID), 0, byteValues, ordinal + 2, 2);
			Buffer.BlockCopy(BitConverter.GetBytes(valueCountOrderBy2Term), 0, byteValues, ordinal + 4, 8);
			if (byteValuesArray.Length <= 8)
			{
				Buffer.BlockCopy(byteValuesArray, 0, byteValues, ordinal + 12, byteValuesArray.Length);
			}
			else
			{
				long ordinal2 = positionFsTerm;
				foreach (byte[] elements in byteValuesList)
				{
					ordinal2 += elements.Length;
				}
				Buffer.BlockCopy(BitConverter.GetBytes((ulong)ordinal2), 0, byteValues, ordinal + 12, 8);
				byteValuesList.Add(byteValuesArray);
			}
			ordinal += 20;
		}
		fs.Write(BitConverter.GetBytes((ulong)count));
		fs.Write(byteValues);
		long position = fs.Position;
		fs.Write(BitConverter.GetBytes(0uL));
		foreach (byte[] elementsArray in byteValuesList)
		{
			fs.Write(elementsArray);
		}
		return position;
	}

	public static string DebugText(string file, bool onlyBaseDirectory = true)
	{
		using TiffFile tiffFile = new TiffFile(file);
		return tiffFile.DebugText(onlyBaseDirectory);
	}

	public string DebugText(bool onlyBaseDirectory = true)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int count = Directories.Count;
		stringBuilder.AppendLine("Filename: " + Filename.InDoubleQuotes());
		stringBuilder.AppendLine("Directories: " + count);
		for (int i = 0; i < count; i++)
		{
			TagMetadata metadata = Directories[i].Metadata;
			stringBuilder.AppendLine("Directory" + i.ToString().InSquareBrackets());
			PropertyInfo[] properties = metadata.GetType().GetProperties();
			foreach (PropertyInfo propertyInfo in properties)
			{
				string name = propertyInfo.Name;
				object getValue = propertyInfo.GetValue(metadata);
				if (getValue != null)
				{
					stringBuilder.AppendLine("  " + name + ": " + getValue.ToString());
				}
			}
			if (onlyBaseDirectory)
			{
				break;
			}
		}
		return stringBuilder.ToString();
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		if (disposing)
		{
			if (_cleanFsPool)
			{
				_fsPool.ClearAll((Stream fs) =>
				{
					fs.Dispose();
				});
			}
			_ovr?.Dispose();
		}
		_isDisposed = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
	}
}
