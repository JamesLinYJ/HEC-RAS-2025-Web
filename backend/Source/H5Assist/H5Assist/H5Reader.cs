using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance;
using H5Assist.Caching;
using H5Assist.Chunking;
using H5Assist.Identifiers;
using HDF.PInvoke;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Compression;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;
using Utility.Threading;

namespace H5Assist;

[DebuggerDisplay("{System.IO.Path.GetFileName(Filename)}")]
public class H5Reader : IDisposable
{
	static H5Reader()
	{
		NativeDependencies.ResolvePathToExternalDependencies();
	}

	public static class MessageCodes
	{
		public static readonly MessageClass FileCorrupt = new MessageClass("H5_File_Corrupt", MessageSeverity.Error, "{Filename} is corrupt and could not be opened.");

		public static readonly MessageClass DatasetNotFound = new MessageClass("H5_Dataset_NotFound", MessageSeverity.Error, "{Filename} is missing required dataset {DatasetName}.");

		public static readonly MessageClass GroupNotFound = new MessageClass("H5_Group_NotFound", MessageSeverity.Error, "{Filename} is missing required group {GroupName}.");

		public static readonly MessageClass AttributeNotFound = new MessageClass("H5_Attribute_NotFound", MessageSeverity.Error, "{Filename}: {GroupOrDatasetName} is missing required attribute {AttributeName} (Type {ExpectedTypeName}).");

		public static readonly MessageClass AttributeIncorrectType = new MessageClass("H5_Attribute_IncorrectType", MessageSeverity.Error, "{Filename}: {GroupOrDatasetName}: Attribute {AttributeName} has an unexpected type. Expected: {ExpectedTypeName}, Actual: {ActualTypeName}.");

		public static readonly MessageClass DatasetIncorrectType = new MessageClass("H5_Dataset_IncorrectType", MessageSeverity.Error, "{Filename}: {GroupOrDatasetName} has an unexpected type. Expected: {ExpectedTypeName}, Actual: {ActualTypeName}.");

		public static readonly MessageClass DatasetIncorrectRank = new MessageClass("H5_Dataset_IncorrectDims", MessageSeverity.Error, "{Filename}: {DatasetName} doesn't have the expected rank. Expected: {ExpectedDimCount}, Actual: {ActualDimCount}.");

		public static readonly MessageClass CompoundTableMissingRequiredColumn = new MessageClass("H5_CompoundTable_MissingRequiredColumn", MessageSeverity.Error, "{Filename}: {DatasetName} is missing required column {ColumnName} of type {ColumnType}.");

		public static Message GetFileCorrupt(string filename)
		{
			return FileCorrupt.Create(filename);
		}

		public static Message GetDatasetNotFound(string filename, string datasetName)
		{
			return DatasetNotFound.Create(filename, datasetName);
		}

		public static Message GetGroupNotFound(string filename, string groupName)
		{
			return GroupNotFound.Create(filename, groupName);
		}

		public static Message GetAttributeNotFound(string filename, string groupOrDataset, string attributeName, string expectedType)
		{
			return AttributeNotFound.Create(filename, groupOrDataset, attributeName, expectedType);
		}

		public static Message GetAttributeIncorrectType(string filename, string groupOrDataset, string attributeName, string expectedType, string actualType)
		{
			return AttributeIncorrectType.Create(filename, groupOrDataset, attributeName, expectedType, actualType);
		}

		public static Message GetDatasetIncorrectType(string filename, string groupOrDataset, string expectedType, string actualType)
		{
			return DatasetIncorrectType.Create(filename, groupOrDataset, expectedType, actualType);
		}

		public static Message GetDatasetIncorrectRank(string filename, string datasetName, int expectedDims, int actualDims)
		{
			return AttributeNotFound.Create(filename, datasetName, expectedDims, actualDims);
		}

		public static Message GetCompoundTableMissingRequiredColumn(string filename, string datasetName, string colName, Type colType)
		{
			return CompoundTableMissingRequiredColumn.Create(filename, datasetName, colName, colType);
		}
	}

	private readonly struct ColumnReadOrder
	{
		private readonly int[] _permutation;

		private ColumnReadOrder(int[] permutation)
		{
			_permutation = permutation;
		}

		public int OriginalIndex(int sortedPosition)
		{
			if (_permutation != null)
			{
				return _permutation[sortedPosition];
			}
			return sortedPosition;
		}

		public static ColumnReadOrder Create(ReadOnlySpan<int> columnIndexes)
		{
			for (int i = 1; i < columnIndexes.Length; i++)
			{
				if (columnIndexes[i] < columnIndexes[i - 1])
				{
					return new ColumnReadOrder(BuildPermutation(columnIndexes));
				}
			}
			return new ColumnReadOrder(null);
		}

		private static int[] BuildPermutation(ReadOnlySpan<int> columnIndexes)
		{
			int[] permutation = new int[columnIndexes.Length];
			for (int i = 0; i < permutation.Length; i++)
			{
				permutation[i] = i;
			}
			Array.Sort(columnIndexes.ToArray(), permutation);
			return permutation;
		}
	}

	protected long FileId = -1L;

	private readonly object _handleLifecycleSync = new object();

	private static readonly ILogger _logger = LogHelper.GetLogger("HDF5");

	protected string _filename;

	public const string Root = "/";

	public const string PathSeparator = "/";

	internal static readonly string CURRENT_LOCATION = ".";

	internal static readonly byte[] CURRENT_LOCATION_B = new byte[1] { 46 };

	public static char[] PathSeparatorChars = new char[2] { '/', '\\' };

	public static char[] IllegalNameChars = new char[2] { '/', '\\' };

	public const string JaggedStartAttribute = "Start";

	public const string JaggedCountAttribute = "Count";

	public static readonly int CHUNK_CACHE_SIZE_BYTES = 10485760;

	private readonly Dictionary<string, DatasetParameters> _parameters = new Dictionary<string, DatasetParameters>();

	private int _disposeState;

	private static readonly bool _trackObjectLifecycle = false;

	private static readonly List<H5Reader> _h5ReadersOpened = new List<H5Reader>();

	private readonly bool _isReadOnly;

	private ConcurrentDictionary<H5DatasetID, long[]> _cachedChunkSizes = new ConcurrentDictionary<H5DatasetID, long[]>();

	protected static readonly ThreadLocal<byte[]> _compressionBuffers = new ThreadLocal<byte[]>(() => new byte[256]);

	private const long StreamDatasetBandTargetBytes = 8388608L;

	internal const string MessageLogStructureSuffix = " (Structure)";

	internal const string MessageLogDataSuffix = " (Data)";

	internal const string MessageLogTimeColumn = "Time (Ticks)";

	internal const string MessageLogSeverityColumn = "Severity";

	internal const string MessageLogCodeColumn = "Code";

	internal const string MessageLogDataLengthColumn = "Data Length";

	public long H5FileID => Interlocked.Read(ref FileId);

	public static ILogger Logger => _logger;

	public string Filename => _filename;

	public bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

	public bool SuccessfullyOpened => Interlocked.Read(ref FileId) >= 0;

	public static bool IsCompiledWithThreadSafety()
	{
		bool is_ts = false;
		if (H5.is_library_threadsafe(ref is_ts) != 0)
		{
			throw new Exception("Error invoking H5 - possible DLL/Library error.");
		}
		return is_ts;
	}

	public H5Reader(string filename)
		: this(filename, 0, SWMR_Mode: false)
	{
	}

	public H5Reader(string filename, int cacheSizeBytes, bool SWMR_Mode)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		_filename = filename;
		if (cacheSizeBytes <= 0)
		{
			uint ordinal = 0u;
			if (SWMR_Mode)
			{
				ordinal |= 0x40;
			}
			FileId = H5F.open(filename, ordinal, 0L);
		}
		else
		{
			long plistWithCacheSize = GetPlistWithCacheSize(cacheSizeBytes);
			FileId = H5F.open(filename, 0u, plistWithCacheSize);
			H5P.close(plistWithCacheSize);
		}
		_isReadOnly = true;
		_logger.LogInformation("Opened {File} for reading.", filename);
		if (_trackObjectLifecycle)
		{
			_logger.LogTrace("\"{File}\" opened. {StackTrace}", Filename, Environment.StackTrace);
			lock (_h5ReadersOpened)
			{
				_h5ReadersOpened.Add(this);
			}
		}
	}

	protected H5Reader()
	{
		_isReadOnly = false;
		if (_trackObjectLifecycle)
		{
			_logger.LogTrace("\"{File}\" opened. {StackTrace}", Filename, Environment.StackTrace);
			lock (_h5ReadersOpened)
			{
				_h5ReadersOpened.Add(this);
			}
		}
	}

	public unsafe static string GetLastError()
	{
		long current_stack = H5E.get_current_stack();
		byte[] byteValues = new byte[4096];
		fixed (byte* stream = byteValues)
		{
			H5E.print(current_stack, (nint)stream);
		}
		return Encoding.UTF8.GetString(byteValues);
	}

	private static H5Reader OpenInMemory(string identifier, int bufferSizeIncrements = 1024)
	{
		long create = H5P.create(H5P.FILE_ACCESS);
		if (H5P.set_fapl_core(create, bufferSizeIncrements, 0u) < 0)
		{
			throw new Exception("Error setting File Access Property List");
		}
		long fileId = H5F.create(identifier, 0u, 0L, create);
		if (fileId < 0)
		{
			throw new Exception("Error creating in-memory file.");
		}
		H5P.close(create);
		return new H5Reader
		{
			FileId = fileId
		};
	}

	protected long GetPlistWithCacheSize(int cacheSizeBytes)
	{
		long create = H5P.create(H5P.FILE_ACCESS);
		H5P.set_cache(create, 0, 521, cacheSizeBytes, 0.75);
		return create;
	}

	~H5Reader()
	{
		try
		{
			Dispose(disposing: false);
		}
		catch (Exception exception)
		{
			// Exceptions must never escape a finalizer and terminate the process.
			// The HDF5 call itself still reports native diagnostics; guard logging
			// separately because a configured logger provider can also throw.
			try
			{
				_logger.LogError(exception, "Failed to release HDF5 handle for {File} during finalization.", _filename);
			}
			catch
			{
				// Finalizers must not propagate failures from either native cleanup or logging.
			}
		}
	}

	public void Dispose()
	{
		try
		{
			Dispose(disposing: true);
		}
		finally
		{
			GC.SuppressFinalize(this);
		}
	}

	private void Dispose(bool disposing)
	{
		long fileId;
		lock (_handleLifecycleSync)
		{
			if (Interlocked.Exchange(ref _disposeState, 1) != 0)
			{
				return;
			}
			fileId = Interlocked.Exchange(ref FileId, -1L);
		}

		if (_trackObjectLifecycle)
		{
			lock (_h5ReadersOpened)
			{
				_h5ReadersOpened.Remove(this);
			}
		}

		if (fileId < 0)
		{
			_logger.LogTrace("No open HDF5 handle remained for {File}.", Filename);
			return;
		}

		int close = H5F.close(fileId);
		if (!disposing)
		{
			_logger.LogWarning("\"{File}\" was not properly disposed.", _filename);
		}
		if (close < 0)
		{
			_logger.LogError("HDF5 file close failed for {File} (ID {FileId}, status {Code}).",
				Filename, fileId, close);
		}
		else
		{
			string? stackTrace = _trackObjectLifecycle ? Environment.StackTrace : null;
			_logger.LogTrace("{File} closed with status {Code}. {StackTrace}", Filename, close, stackTrace);
		}
	}

	public void SaveAs(string filename)
	{
		if (File.Exists(filename))
		{
			File.Delete(filename);
		}
		using H5Writer writer = new H5Writer(filename);
		CopyTo(writer, "/", "/");
	}

	public void AssertLoadedFileID()
	{
		if (FileId < 0)
		{
			throw new Exception("Did not successfully open \"" + _filename + "\", it is potentially locked or corrupt.");
		}
	}

	public List<string> GetAttributeNames(string objectName)
	{
		List<string> attributeNames = new List<string>();
		long open = H5O.open(FileId, objectName, 0L);
		H5O.info_t oinfo = default;
		H5O.get_info(open, ref oinfo);
		int num_attrs = (int)oinfo.num_attrs;
		Encoding aSCIIEncoding = Encoding.UTF8;
		byte[] byteValues = new byte[20];
		for (int i = 0; i < num_attrs; i++)
		{
			int get_name_by_idx = (int)H5A.get_name_by_idx(open, CURRENT_LOCATION_B, H5.index_t.NAME, H5.iter_order_t.NATIVE, (ulong)i, byteValues, byteValues.Length, 0L);
			get_name_by_idx++;
			if (get_name_by_idx > byteValues.Length)
			{
				byteValues = new byte[get_name_by_idx];
				H5A.get_name_by_idx(open, CURRENT_LOCATION_B, H5.index_t.NAME, H5.iter_order_t.NATIVE, (ulong)i, byteValues, byteValues.Length, 0L);
			}
			attributeNames.Add(aSCIIEncoding.GetString(byteValues, 0, get_name_by_idx).Trim(Convert.ToChar(0)));
		}
		H5O.close(open);
		return attributeNames;
	}

	public Type GetAttributeType(string objectName, string attributeName)
	{
		long open = H5O.open(FileId, objectName, 0L);
		if (H5A.exists(open, attributeName) <= 0)
		{
			throw new Exception("Attribute " + attributeName + " DNE.");
		}
		Type attributeType = GetAttributeType(open, attributeName);
		H5O.close(open);
		return attributeType;
	}

	public Dictionary<string, Type> GetAttributeNamesAndTypes(string objectName)
	{
		Dictionary<string, Type> attributeNamesAndTypes = new Dictionary<string, Type>();
		long open = H5O.open(FileId, objectName, 0L);
		H5O.info_t oinfo = default;
		H5O.get_info(open, ref oinfo);
		int num_attrs = (int)oinfo.num_attrs;
		Encoding aSCIIEncoding = Encoding.UTF8;
		byte[] byteValues = new byte[20];
		for (int i = 0; i < num_attrs; i++)
		{
			int get_name_by_idx = (int)H5A.get_name_by_idx(open, CURRENT_LOCATION_B, H5.index_t.NAME, H5.iter_order_t.NATIVE, (ulong)i, byteValues, byteValues.Length, 0L);
			get_name_by_idx++;
			if (get_name_by_idx > byteValues.Length)
			{
				byteValues = new byte[get_name_by_idx];
				H5A.get_name_by_idx(open, CURRENT_LOCATION_B, H5.index_t.NAME, H5.iter_order_t.NATIVE, (ulong)i, byteValues, byteValues.Length, 0L);
			}
			string trim = aSCIIEncoding.GetString(byteValues, 0, get_name_by_idx).Trim(Convert.ToChar(0));
			Type attributeType = GetAttributeType(open, trim);
			attributeNamesAndTypes[trim] = attributeType;
		}
		H5O.close(open);
		return attributeNamesAndTypes;
	}

	public int GetAttributeNDims(string objectName, string attributeName)
	{
		long open = H5O.open(FileId, objectName, 0L);
		long attr_id = H5A.open(open, attributeName, 0L);
		long space_id = H5A.get_space(attr_id);
		int result = H5S.get_simple_extent_ndims(space_id);
		H5A.close(attr_id);
		H5S.close(space_id);
		H5O.close(open);
		return result;
	}

	public int GetNumAttributes(string datasetOrGroupName)
	{
		long open = H5O.open(FileId, datasetOrGroupName, 0L);
		int numAttributes = GetNumAttributes(open);
		H5O.close(open);
		return numAttributes;
	}

	private int GetNumAttributes(long objID)
	{
		H5O.info_t oinfo = default;
		H5O.get_info(objID, ref oinfo);
		return (int)oinfo.num_attrs;
	}

	public static int ColumnChunkSize(int dsetHeight, int dsetWidth, int maxBytesPerWrite, int bytesPerValue)
	{
		int dsetHeightTimesBytesPerValue = dsetHeight * bytesPerValue;
		return Math.Max(Math.Min((int)Math.Ceiling((double)maxBytesPerWrite / (double)dsetHeightTimesBytesPerValue), dsetWidth), 1);
	}

	public static int RowChunkSize(int dsetHeight, int dsetWidth, int maxBytesPerWrite, int bytesPerValue)
	{
		int dsetWidthTimesBytesPerValue = dsetWidth * bytesPerValue;
		return Math.Max(Math.Min((int)Math.Ceiling((double)maxBytesPerWrite / (double)dsetWidthTimesBytesPerValue), dsetHeight), 1);
	}

	public long[] GetChunkSize(string datasetName)
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: false, needSpace: true, needCreatePropertyList: true);
		return GetChunkSize(dataset);
	}

	public long[] GetChunkSize(H5DatasetID dataset)
	{
		long spaceId = dataset.SpaceId;
		long createPropertyListId = dataset.CreatePropertyListId;
		int get_simple_extent_ndims = H5S.get_simple_extent_ndims(spaceId);
		ulong[] ulongs = new ulong[get_simple_extent_ndims];
		try
		{
			if (H5P.get_chunk(createPropertyListId, get_simple_extent_ndims, ulongs) < 0)
			{
				throw new Exception("GetChunk error");
			}
		}
		catch
		{
			ulongs = new ulong[0];
		}
		return UlongToLong(ulongs);
	}

	protected static long[] UlongToLong(ulong[] arr)
	{
		long[] longs = new long[arr.Length];
		for (int i = 0; i < arr.Length; i++)
		{
			longs[i] = (long)arr[i];
		}
		return longs;
	}

	protected static ulong[] LongToULong(long[] arr)
	{
		ulong[] ulongs = new ulong[arr.Length];
		for (int i = 0; i < arr.Length; i++)
		{
			ulongs[i] = (ulong)arr[i];
		}
		return ulongs;
	}

	public long[] GetCachedChunkSize(H5DatasetID dataset)
	{
		if (_cachedChunkSizes.TryGetValue(dataset, out var localValue))
		{
			return localValue;
		}
		lock (_cachedChunkSizes)
		{
			long[] chunkSize = GetChunkSize(dataset);
			_cachedChunkSizes[dataset] = chunkSize;
			return chunkSize;
		}
	}

	public List<string> GetGroupNames(string groupPath)
	{
		return GetLinkNames(groupPath, H5O.type_t.GROUP);
	}

	public List<string> GetFullyQualifiedGroupNames(string groupPath)
	{
		string groupPath2 = groupPath;
		if (!groupPath2.EndsWith("/") && !groupPath2.EndsWith("\\"))
		{
			groupPath2 += "/";
		}
		List<string> groupNames = GetGroupNames(groupPath);
		for (int i = 0; i < groupNames.Count; i++)
		{
			groupNames[i] = groupPath2 + groupNames[i];
		}
		return groupNames;
	}

	public List<string> GetDatasetNames(string groupPath)
	{
		return GetLinkNames(groupPath, H5O.type_t.DATASET);
	}

	public int GetDatasetCount(string groupPath)
	{
		using H5GroupID h5GroupID = H5GroupID.Open(FileId, groupPath);
		if (!h5GroupID.OpenedSuccessfully)
		{
			return 0;
		}
		return h5GroupID.GetLinkCount(H5O.type_t.DATASET);
	}

	public int GetGroupCount(string groupPath)
	{
		using H5GroupID h5GroupID = H5GroupID.Open(FileId, groupPath);
		if (!h5GroupID.OpenedSuccessfully)
		{
			return 0;
		}
		return h5GroupID.GetLinkCount(H5O.type_t.GROUP);
	}

	public List<string> GetFullyQualifiedDatasetNames(string groupPath)
	{
		string groupPath2 = groupPath;
		if (!groupPath2.EndsWith("/") && !groupPath2.EndsWith("\\"))
		{
			groupPath2 += "/";
		}
		List<string> datasetNames = GetDatasetNames(groupPath);
		for (int i = 0; i < datasetNames.Count; i++)
		{
			datasetNames[i] = groupPath2 + datasetNames[i];
		}
		return datasetNames;
	}

	protected List<string> GetLinkNames(string groupPath, H5O.type_t linkType)
	{
		using H5GroupID h5GroupID = H5GroupID.Open(FileId, groupPath);
		return h5GroupID.GetLinkNames(linkType);
	}

	public static string GetParentGroupName(string groupOrDataset)
	{
		if (string.IsNullOrEmpty(groupOrDataset) || groupOrDataset == "/")
		{
			return string.Empty;
		}
		string[] parts = groupOrDataset.Split(PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries);
		string path = "/";
		for (int i = 0; i < parts.Length - 1; i++)
		{
			path = path + parts[i] + "/";
		}
		return path;
	}

	public int GetDatasetNDims(string datasetName)
	{
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: false, needSpace: true);
		return H5S.get_simple_extent_ndims(h5DatasetID.SpaceId);
	}

	public long[] GetDatasetDimensions(string datasetName)
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: false, needSpace: true);
		return GetDatasetDimensions(dataset);
	}

	public long[] GetDatasetDimensions(H5DatasetID dataset)
	{
		return UlongToLong(GetDimensions(dataset.SpaceId));
	}

	public Type GetDatasetType(string datasetName)
	{
		using H5DatasetID dsetId = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: false);
		return GetDatasetType(dsetId);
	}

	public Type GetDatasetType(H5DatasetID dsetId)
	{
		return H5DataTypeID.TypeConvert(dsetId, 0);
	}

	public bool DatasetIsVariableLength(string datasetName)
	{
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: false);
		return H5T.is_variable_str(h5DatasetID.DataType.TypeId) > 0 || H5T.get_size(h5DatasetID.DataType.TypeId) == H5T.VARIABLE;
	}

	public bool PathIsExternalLink(string absolutePath)
	{
		if (!ObjectExists(absolutePath))
		{
			return false;
		}
		H5L.info_t linfo = default;
		if (H5L.get_info(FileId, absolutePath, ref linfo, 0L) != -1)
		{
			return linfo.type == H5L.type_t.EXTERNAL;
		}
		return false;
	}

	public bool TryGetExternalLinkValue(string absolutePath, out string fileName, out string dataSet)
	{
		fileName = "";
		dataSet = "";
		if (!PathIsExternalLink(absolutePath))
		{
			return false;
		}
		H5L.info_t linfo = default;
		H5L.get_info(FileId, absolutePath, ref linfo, 0L);
		byte[] byteValues = new byte[((IntPtr)linfo.u.val_size).ToInt32()];
		GCHandle gCHandle = GCHandle.Alloc(byteValues, GCHandleType.Pinned);
		H5L.get_val(FileId, absolutePath, gCHandle.AddrOfPinnedObject(), linfo.u.val_size, 0L);
		string[] parts = Encoding.UTF8.GetString(byteValues).Split('\0');
		fileName = parts[1];
		dataSet = parts[2];
		gCHandle.Free();
		return true;
	}

	public bool ExistsAsGroup(string absolutePath)
	{
		return ObjectOfTypeExists(absolutePath, H5O.type_t.GROUP);
	}

	public bool ExistsAsDataset(string absolutePath)
	{
		return ObjectOfTypeExists(absolutePath, H5O.type_t.DATASET);
	}

	public bool ExistsAsAttribute(string absolutePath, string attributeName)
	{
		if (!ObjectExists(absolutePath))
		{
			return false;
		}
		long open = H5O.open(FileId, absolutePath, 0L);
		int exists_by_name = H5A.exists_by_name(open, CURRENT_LOCATION, attributeName, 0L);
		H5O.close(open);
		return exists_by_name > 0;
	}

	private bool ObjectOfTypeExists(string absolutePath, H5O.type_t dsetOrGroup)
	{
		if (!ObjectExists(absolutePath))
		{
			return false;
		}
		H5O.info_t oinfo = default;
		H5O.get_info_by_name(FileId, absolutePath, ref oinfo, 0L);
		if (oinfo.type == dsetOrGroup)
		{
			return true;
		}
		return false;
	}

	protected bool ObjectExists(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return false;
		}
		string[] newStrings = path.Split('/');
		StripEmpty(ref newStrings);
		string textValue = "";
		string[] newStringsArray = newStrings;
		foreach (string newString in newStringsArray)
		{
			if (textValue.Length > 0)
			{
				textValue += "/";
			}
			textValue += newString;
			try
			{
				if (H5L.exists(FileId, textValue, 0L) <= 0)
				{
					return false;
				}
			}
			catch
			{
				return false;
			}
		}
		return true;
	}

	public int GetCacheSize()
	{
		int mdc_nelmts = 0;
		nint rdcc_nelmts = IntPtr.Zero;
		nint rdcc_nbytes = IntPtr.Zero;
		double rdcc_w = 0.0;
		long get_access_plist = H5F.get_access_plist(FileId);
		H5P.get_cache(get_access_plist, ref mdc_nelmts, ref rdcc_nelmts, ref rdcc_nbytes, ref rdcc_w);
		H5P.close(get_access_plist);
		return (int)rdcc_nbytes;
	}

	private void AttributeIntegrityCheck<T>(H5AttributeID attr, int nDims, [CallerMemberName] string callerName = "")
	{
		if (H5S.get_simple_extent_ndims(attr.SpaceId) != nDims)
		{
			throw new IncorrectDimensionException(attr.ObjectName + "." + attr.AttributeName, nDims, callerName);
		}
		if (!attr.DataType.TypeMatches<T>())
		{
			throw new IncorrectTypeException(attr.ObjectName + "." + attr.AttributeName, typeof(T), callerName);
		}
	}

	private unsafe void H5AReadInternal<T>(long attributeId, long typeId, T[] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		H5A.read(attributeId, typeId, (nint)memoryHandle.Pointer);
	}

	private unsafe void H5AReadInternal<T>(long attributeId, long typeId, T[,] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		H5A.read(attributeId, typeId, (nint)memoryHandle.Pointer);
	}

	private unsafe void H5AReadInternal<T>(long attributeId, long typeId, T[,,] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		H5A.read(attributeId, typeId, (nint)memoryHandle.Pointer);
	}

	public unsafe void ReadAttributeScalar<T>(string objectName, string attributeName, out T localValue2) where T : unmanaged
	{
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		AttributeIntegrityCheck<T>(h5AttributeID, 0, "ReadAttributeScalar");
		long attributeId = h5AttributeID.AttributeId;
		long typeId = h5AttributeID.DataType.TypeId;
		_ = h5AttributeID.SpaceId;
		T localValue = default;
		H5A.read(attributeId, typeId, (nint)(&localValue));
		localValue2 = localValue;
	}

	public void ReadAttributeScalar(string objectName, string attributeName, out string trim)
	{
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		AttributeIntegrityCheck<string>(h5AttributeID, 0, "ReadAttributeScalar");
		long attributeId = h5AttributeID.AttributeId;
		long typeId = h5AttributeID.DataType.TypeId;
		_ = h5AttributeID.SpaceId;
		Encoding utf8 = Encoding.UTF8;
		byte[] byteValues = new byte[(int)H5T.get_size(typeId)];
		H5AReadInternal(attributeId, typeId, byteValues);
		trim = utf8.GetString(byteValues).Trim('\0');
	}

	public void ReadAttribute<T>(string objectName, string attributeName, out T[] values) where T : unmanaged
	{
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		h5AttributeID.GetNativeIDs(out var attrID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nElements);
		values = new T[nElements];
		H5AReadInternal(attrID, datatypeID, values);
	}

	public void ReadAttribute(string objectName, string attributeName, out string[] values)
	{
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		h5AttributeID.GetNativeIDs(out var attrID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nElements);
		values = new string[nElements];
		int get_size = (int)H5T.get_size(datatypeID);
		byte[] byteValues = new byte[(int)nElements * get_size];
		H5AReadInternal(attrID, datatypeID, byteValues);
		ParseString(byteValues, get_size, values);
	}

	public void ReadAttribute<T>(string objectName, string attributeName, out T[,] values) where T : unmanaged
	{
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		h5AttributeID.GetNativeIDs(out var attrID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nRows, out var nCols);
		values = new T[(int)checked((nint)nRows), (int)checked((nint)nCols)];
		H5AReadInternal(attrID, datatypeID, values);
	}

	public void ReadAttribute(string objectName, string attributeName, out string[,] values)
	{
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		h5AttributeID.GetNativeIDs(out var attrID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nRows, out var nCols);
		values = new string[(int)checked((nint)nRows), (int)checked((nint)nCols)];
		int get_size = (int)H5T.get_size(datatypeID);
		byte[,] byteValues = new byte[(uint)nRows, (int)nCols * get_size];
		H5AReadInternal(attrID, datatypeID, byteValues);
		ParseString(byteValues, get_size, values);
	}

	public void ReadAttribute<T>(string objectName, string attributeName, out T[,,] values)
	{
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		h5AttributeID.GetNativeIDs(out var attrID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nPlanes, out var nRows, out var nCols);
		values = new T[(int)checked((nint)nPlanes), (int)checked((nint)nRows), (int)checked((nint)nCols)];
		H5AReadInternal(attrID, datatypeID, values);
	}

	public bool TryReadAttributeScalar<T>(string objectName, string attributeName, out T localValue) where T : unmanaged
	{
		try
		{
			if (!ExistsAsAttribute(objectName, attributeName))
			{
				localValue = default;
				return false;
			}
			ReadAttributeScalar(objectName, attributeName, out localValue);
			return true;
		}
		catch
		{
			localValue = default;
			return false;
		}
	}

	public bool TryReadAttributeScalar(string objectName, string attributeName, out string stringValue)
	{
		try
		{
			if (!ExistsAsAttribute(objectName, attributeName))
			{
				stringValue = null;
				return false;
			}
			ReadAttributeScalar(objectName, attributeName, out stringValue);
			return true;
		}
		catch
		{
			stringValue = null;
			return false;
		}
	}

	public bool TryReadAttribute<T>(string objectName, string attributeName, out T[] values) where T : unmanaged
	{
		try
		{
			if (!ExistsAsAttribute(objectName, attributeName))
			{
				values = null;
				return false;
			}
			ReadAttribute(objectName, attributeName, out values);
			return true;
		}
		catch
		{
			values = null;
			return false;
		}
	}

	public bool TryReadAttribute<T>(string objectName, string attributeName, out T[,] values) where T : unmanaged
	{
		try
		{
			if (!ExistsAsAttribute(objectName, attributeName))
			{
				values = null;
				return false;
			}
			ReadAttribute(objectName, attributeName, out values);
			return true;
		}
		catch
		{
			values = null;
			return false;
		}
	}

	public bool TryReadEnum<T>(string objectName, string attributeName, out T localValue) where T : struct, Enum
	{
		try
		{
			if (!ExistsAsAttribute(objectName, attributeName))
			{
				localValue = default;
				return false;
			}
			if (GetAttributeType(objectName, attributeName) != typeof(string))
			{
				localValue = default;
				return false;
			}
			ReadAttributeScalar(objectName, attributeName, out var textValue);
			return DiskNameAttribute.TryDeserialize<T>(textValue, out localValue);
		}
		catch
		{
			localValue = default;
			return false;
		}
	}

	public string TryReadStringAttributeScalar(string setOrGroupName, string attributeName, string defaultValue = "")
	{
		if (TryReadAttributeScalar(setOrGroupName, attributeName, out var textValue))
		{
			return textValue;
		}
		return defaultValue;
	}

	public Dictionary<string, object> ReadAllAttributes(string dsetOrGroupname)
	{
		Dictionary<string, object> allAttributes = new Dictionary<string, object>();
		if (!ObjectExists(dsetOrGroupname))
		{
			return allAttributes;
		}
		foreach (KeyValuePair<string, Type> attributeNamesAndType in GetAttributeNamesAndTypes(dsetOrGroupname))
		{
			string key = attributeNamesAndType.Key;
			Type type = attributeNamesAndType.Value;
			if (type == null)
			{
				allAttributes.Add(key, null);
				continue;
			}
			int arrayRank = 0;
			if (type.IsArray)
			{
				arrayRank = type.GetArrayRank();
			}
			if (arrayRank >= 1)
			{
				type = type.GetElementType();
			}
			switch (arrayRank)
			{
			case 0:
				if (type == typeof(byte))
				{
					ReadAttributeScalar(dsetOrGroupname, key, out byte localValue);
					allAttributes.Add(key, localValue);
				}
				else if (type == typeof(short))
				{
					ReadAttributeScalar(dsetOrGroupname, key, out short ordinal);
					allAttributes.Add(key, ordinal);
				}
				else if (type == typeof(int))
				{
					ReadAttributeScalar(dsetOrGroupname, key, out int ordinal2);
					allAttributes.Add(key, ordinal2);
				}
				else if (type == typeof(long))
				{
					ReadAttributeScalar(dsetOrGroupname, key, out long ordinal3);
					allAttributes.Add(key, ordinal3);
				}
				else if (type == typeof(float))
				{
					ReadAttributeScalar(dsetOrGroupname, key, out float computed);
					allAttributes.Add(key, computed);
				}
				else if (type == typeof(double))
				{
					ReadAttributeScalar(dsetOrGroupname, key, out double computed2);
					allAttributes.Add(key, computed2);
				}
				else if (type == typeof(string))
				{
					ReadAttributeScalar(dsetOrGroupname, key, out var textValue);
					allAttributes.Add(key, textValue);
				}
				else if (type == typeof(bool))
				{
					ReadAttributeScalar(dsetOrGroupname, key, out bool condition);
					allAttributes.Add(key, condition);
				}
				else
				{
					_logger.LogWarning("Could not read attribute {AttributeName} of type {AttributeType}.", key, type.Name);
				}
				break;
			case 1:
				if (type == typeof(byte))
				{
					ReadAttribute(dsetOrGroupname, key, out byte[] values9);
					allAttributes.Add(key, values9);
				}
				else if (type == typeof(short))
				{
					ReadAttribute(dsetOrGroupname, key, out short[] values10);
					allAttributes.Add(key, values10);
				}
				else if (type == typeof(int))
				{
					ReadAttribute(dsetOrGroupname, key, out int[] values11);
					allAttributes.Add(key, values11);
				}
				else if (type == typeof(long))
				{
					ReadAttribute(dsetOrGroupname, key, out long[] values12);
					allAttributes.Add(key, values12);
				}
				else if (type == typeof(float))
				{
					ReadAttribute(dsetOrGroupname, key, out float[] values13);
					allAttributes.Add(key, values13);
				}
				else if (type == typeof(double))
				{
					ReadAttribute(dsetOrGroupname, key, out double[] values14);
					allAttributes.Add(key, values14);
				}
				else if (type == typeof(string))
				{
					ReadAttribute(dsetOrGroupname, key, out string[] values15);
					allAttributes.Add(key, values15);
				}
				else if (type == typeof(bool))
				{
					ReadAttribute(dsetOrGroupname, key, out bool[] values16);
					allAttributes.Add(key, values16);
				}
				else
				{
					_logger.LogWarning("Could not read attribute {AttributeName} of type {AttributeType}.", key, type.Name);
				}
				break;
			case 2:
				if (type == typeof(byte))
				{
					ReadAttribute(dsetOrGroupname, key, out byte[,] values);
					allAttributes.Add(key, values);
				}
				else if (type == typeof(short))
				{
					ReadAttribute(dsetOrGroupname, key, out short[,] values2);
					allAttributes.Add(key, values2);
				}
				else if (type == typeof(int))
				{
					ReadAttribute(dsetOrGroupname, key, out int[,] values3);
					allAttributes.Add(key, values3);
				}
				else if (type == typeof(long))
				{
					ReadAttribute(dsetOrGroupname, key, out long[,] values4);
					allAttributes.Add(key, values4);
				}
				else if (type == typeof(float))
				{
					ReadAttribute(dsetOrGroupname, key, out float[,] values5);
					allAttributes.Add(key, values5);
				}
				else if (type == typeof(double))
				{
					ReadAttribute(dsetOrGroupname, key, out double[,] values6);
					allAttributes.Add(key, values6);
				}
				else if (type == typeof(string))
				{
					ReadAttribute(dsetOrGroupname, key, out string[,] values7);
					allAttributes.Add(key, values7);
				}
				else if (type == typeof(bool))
				{
					ReadAttribute(dsetOrGroupname, key, out bool[,] values8);
					allAttributes.Add(key, values8);
				}
				else
				{
					_logger.LogWarning("Could not read attribute {AttributeName} of type {AttributeType}.", key, type.Name);
				}
				break;
			default:
				throw new NotSupportedException("Cannot read attributes of rank " + arrayRank);
			}
		}
		return allAttributes;
	}

	public DatasetParameters GetDatasetParameters(string dset)
	{
		if (_isReadOnly)
		{
			DatasetParameters parameters;
			lock (_parameters)
			{
				if (_parameters.TryGetValue(dset, out parameters))
				{
					return parameters;
				}
			}
			parameters.Name = dset;
			using (H5DatasetID h5DatasetID = OpenDataset(dset))
			{
				parameters.Dimensions = UlongToLong(GetDimensions(h5DatasetID.SpaceId));
				parameters.ChunkDimensions = GetChunkSize(h5DatasetID);
			}
			lock (_parameters)
			{
				_parameters[dset] = parameters;
				return parameters;
			}
		}
		using H5DatasetID h5DatasetID2 = OpenDataset(dset);
		return new DatasetParameters
		{
			Dimensions = UlongToLong(GetDimensions(h5DatasetID2.SpaceId)),
			ChunkDimensions = GetChunkSize(h5DatasetID2)
		};
	}

	public bool TryGetDatasetParameters(string dset, out DatasetParameters para)
	{
		if (_isReadOnly)
		{
			DatasetParameters datasetParameters;
			lock (_parameters)
			{
				if (_parameters.TryGetValue(dset, out datasetParameters))
				{
					para = datasetParameters;
					return true;
				}
			}
			datasetParameters.Name = dset;
			using (H5DatasetID h5DatasetID = TryOpenDataset(dset))
			{
				if (h5DatasetID == null)
				{
					para = default;
					return false;
				}
				datasetParameters.Dimensions = UlongToLong(GetDimensions(h5DatasetID.SpaceId));
				datasetParameters.ChunkDimensions = GetChunkSize(h5DatasetID);
			}
			lock (_parameters)
			{
				_parameters[dset] = datasetParameters;
			}
			para = datasetParameters;
			return true;
		}
		using H5DatasetID h5DatasetID2 = TryOpenDataset(dset);
		if (h5DatasetID2 == null)
		{
			para = default;
			return false;
		}
		para = new DatasetParameters
		{
			Dimensions = UlongToLong(GetDimensions(h5DatasetID2.SpaceId)),
			ChunkDimensions = GetChunkSize(h5DatasetID2)
		};
		return true;
	}

	private void DatasetIntegrityCheck<T>(H5DatasetID dset, int nDims, [CallerMemberName] string callerName = "")
	{
		if (H5S.get_simple_extent_ndims(dset.SpaceId) != nDims)
		{
			throw new IncorrectDimensionException(dset.DatasetName, nDims, callerName);
		}
		if (!H5DataTypeID.TypeMatch<T>(dset.DataType.TypeId))
		{
			throw new IncorrectTypeException(dset.DatasetName, typeof(T), callerName);
		}
	}

	protected unsafe int H5DReadInternal<T>(long datasetId, long typeId, Memory<T> buffer) where T : unmanaged
	{
		using MemoryHandle memoryHandle = buffer.Pin();
		return H5D.read(datasetId, typeId, 0L, 0L, 0L, (nint)memoryHandle.Pointer);
	}

	protected unsafe int H5DReadInternal<T>(long datasetId, long typeId, T[,] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5D.read(datasetId, typeId, 0L, 0L, 0L, (nint)memoryHandle.Pointer);
	}

	protected unsafe int H5DReadInternal<T>(long datasetId, long typeId, T[,,] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5D.read(datasetId, typeId, 0L, 0L, 0L, (nint)memoryHandle.Pointer);
	}

	protected unsafe int H5DReadInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, Memory<T> buffer)
	{
		using MemoryHandle memoryHandle = buffer.Pin();
		return H5D.read(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, (nint)memoryHandle.Pointer);
	}

	protected unsafe int H5DReadInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, T[,] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5D.read(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, (nint)memoryHandle.Pointer);
	}

	protected unsafe int H5DReadInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, T[,,] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5D.read(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, (nint)memoryHandle.Pointer);
	}

	private void LogDatasetRead(string datasetName, int elementCount, long startTicks)
	{
		TimeSpan elapsedTime = Stopwatch.GetElapsedTime(startTicks);
		_logger.LogInformation("Read {Dataset} from {File}: {Elements} elements in {Elapsed}", datasetName, _filename, elementCount, elapsedTime);
	}

	public void ReadDataset<T>(string datasetName, out T[] values) where T : unmanaged
	{
		long timestamp = Stopwatch.GetTimestamp();
		using (H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true))
		{
			ReadDataset(dataset, out values);
		}
		LogDatasetRead(datasetName, values.Length, timestamp);
	}

	public void ReadDataset<T>(string datasetName, Memory<T> values) where T : unmanaged
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ReadDataset(dataset, values);
	}

	public void ReadDataset<T>(H5DatasetID dataset, out T[] values) where T : unmanaged
	{
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nElements);
		values = new T[nElements];
		H5DReadInternal<T>(datasetID, datatypeID, values);
	}

	public void ReadDataset<T>(H5DatasetID dataset, Memory<T> values) where T : unmanaged
	{
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nElements);
		if (values.Length != (int)nElements)
		{
			throw new ArgumentException("Values buffer incorrectly sized.");
		}
		H5DReadInternal(datasetID, datatypeID, values);
	}

	public unsafe void ReadDataset(string datasetName, out string[] values)
	{
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nElements);
		values = new string[nElements];
		if (nElements == 0L)
		{
			return;
		}
		if (H5T.is_variable_str(datatypeID) > 0)
		{
			nint[] pointers = new nint[values.Length];
			fixed (nint* buffer = pointers)
			{
				H5DReadInternal<nint>(datasetID, datatypeID, (Memory<nint>)pointers);
				try
				{
					for (int i = 0; i < values.Length; i++)
					{
						byte* text = (byte*)pointers[i];
						if (text == null) { values[i] = string.Empty; continue; }
						int length = 0;
						while (text[length] != 0) length++;
						values[i] = Encoding.UTF8.GetString(text, length);
					}
				}
				finally { H5D.vlen_reclaim(datatypeID, dataspaceID, 0L, (nint)buffer); }
			}
			return;
		}
		int get_size = (int)H5T.get_size(datatypeID);
		byte[] byteValues = new byte[(int)nElements * get_size];
		H5DReadInternal<byte>(datasetID, datatypeID, byteValues);
		ParseString(byteValues, get_size, values);
	}

	public void ReadDatasetAs<T>(string datasetName, out T[] values) where T : unmanaged, IConvertible
	{
		Type datasetType = GetDatasetType(datasetName);
		if (datasetType == typeof(T))
		{
			ReadDataset(datasetName, out values);
			return;
		}
		Array values6 = null;
		if (datasetType == typeof(byte))
		{
			byte[] values2 = null;
			ReadDataset(datasetName, out values2);
			values6 = values2;
		}
		else if (datasetType == typeof(short))
		{
			short[] values3 = null;
			ReadDataset(datasetName, out values3);
			values6 = values3;
		}
		else if (datasetType == typeof(int))
		{
			ReadDataset(datasetName, out int[] values4);
			values6 = values4;
		}
		else
		{
			if (!(datasetType == typeof(long)))
			{
				throw new NotImplementedException();
			}
			long[] values5 = null;
			ReadDataset(datasetName, out values5);
			values6 = values5;
		}
		values = new T[values6.Length];
		for (int i = 0; i < values6.Length; i++)
		{
			values[i] = (T)Convert.ChangeType(values6.GetValue(i), typeof(T));
		}
	}

	public void ReadDataset<T>(string datasetName, out T[,] values) where T : unmanaged
	{
		long timestamp = Stopwatch.GetTimestamp();
		using (H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true))
		{
			ReadDataset(dataset, out values);
		}
		LogDatasetRead(datasetName, values.Length, timestamp);
	}

	public void ReadDataset<T>(H5DatasetID dataset, out T[,] values) where T : unmanaged
	{
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nRows, out var nCols);
		values = new T[(int)checked((nint)nRows), (int)checked((nint)nCols)];
		if (nRows * nCols != 0L)
		{
			H5DReadInternal(datasetID, datatypeID, values);
		}
	}

	public unsafe void ReadDatasetUnsafe<T>(string datasetName, T* buffer) where T : unmanaged
	{
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var _);
		H5D.read(datasetID, datatypeID, 0L, 0L, 0L, (nint)buffer);
	}

	public unsafe void ReadDatasetUnsafe<T>(H5DatasetID dataset, T* buffer) where T : unmanaged
	{
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var _);
		H5D.read(datasetID, datatypeID, 0L, 0L, 0L, (nint)buffer);
	}

	public void ReadDataset(H5DatasetID dataset, out string[,] values)
	{
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nRows, out var nCols);
		values = new string[(int)checked((nint)nRows), (int)checked((nint)nCols)];
		int get_size = (int)H5T.get_size(datatypeID);
		byte[,] byteValues = new byte[(uint)nRows, (int)nCols * get_size];
		H5DReadInternal(datasetID, datatypeID, byteValues);
		ParseString(byteValues, get_size, values);
	}

	public void ReadDataset<T>(string datasetName, out T[,,] values) where T : unmanaged
	{
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		GetDimensions(dataspaceID, out var nPlanes, out var nRows, out var nCols);
		values = new T[(int)checked((nint)nPlanes), (int)checked((nint)nRows), (int)checked((nint)nCols)];
		H5DReadInternal(datasetID, datatypeID, values);
	}

	protected void RowColArgCheck(int startRow, int numRows, int startCol, int numCols, ulong[] size, string datasetName, [CallerMemberName] string callerName = "")
	{
		if (startRow < 0)
		{
			throw new ArgumentException("ERROR: Must have a non-negative starting row for reading dataset." + Context());
		}
		if (numRows <= 0)
		{
			throw new ArgumentException("ERROR: Must have a positive row count for reading dataset." + Context());
		}
		if (startCol < 0)
		{
			throw new ArgumentException("ERROR: Must have a non-negative starting column for reading dataset." + Context());
		}
		if (numCols <= 0)
		{
			throw new ArgumentException("ERROR: Must have a positive column count for reading dataset." + Context());
		}
		if (startRow >= (int)size[0])
		{
			throw new ArgumentException("Start row is beyond extent of dataset." + Context());
		}
		if (numRows > (int)size[0] - startRow)
		{
			throw new ArgumentException("Start row + number of rows goes beyond extent of dataset." + Context());
		}
		if (startCol >= (int)size[1])
		{
			throw new ArgumentException("Start column is beyond extent of dataset." + Context());
		}
		if (numCols > (int)size[1] - startCol)
		{
			throw new ArgumentException("Start column + number of columns goes beyond extent of dataset." + Context());
		}
		string Context()
		{
			return $" Requested rows [{startRow}, {startRow + numRows}) and columns [{startCol}, {startCol + numCols}) of a {size[0]}x{size[1]} dataset \"{datasetName}\" in {callerName} of \"{_filename}\".";
		}
	}

	private void ElementArgCheck(int startElement, int numElements, ulong[] size, string datasetName, [CallerMemberName] string callerName = "")
	{
		if (startElement < 0)
		{
			throw new ArgumentException("ERROR: Must have a non-negative starting element for reading dataset." + Context());
		}
		if (numElements <= 0)
		{
			throw new ArgumentException("ERROR: Must have a positive element count for reading dataset." + Context());
		}
		if (startElement >= (int)size[0])
		{
			throw new ArgumentException("Start element is beyond extent of dataset." + Context());
		}
		if (numElements > (int)size[0] - startElement)
		{
			throw new ArgumentException("Start element + number of elements goes beyond extent of dataset." + Context());
		}
		string Context()
		{
			return $" Requested elements [{startElement}, {startElement + numElements}) of a {size[0]}-element dataset \"{datasetName}\" in {callerName} of \"{_filename}\".";
		}
	}

	public void ReadRow<T>(string dsetName, int row, ref T[,] values) where T : unmanaged
	{
		ReadRows(dsetName, row, 1, ref values);
	}

	public void ReadRow<T>(H5DatasetID d, int row, ref T[,] values) where T : unmanaged
	{
		ReadRows(d, row, 1, ref values);
	}

	public void ReadRows<T>(string datasetName, int startRow, int numRows, ref T[,] values) where T : unmanaged
	{
		using (H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true))
		{
			DatasetIntegrityCheck<T>(h5DatasetID, 2, "ReadRows");
			h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
			ulong[] dimensions = GetDimensions(dataspaceID);
			RowColArgCheck(startRow, numRows, 0, (int)dimensions[1], dimensions, datasetName, "ReadRows");
			Hyperslab.GetRowDims(startRow, numRows, dimensions, out var hyperslabDims, out var offset);
			RedimIfNecessary(ref values, hyperslabDims);
			using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, hyperslabDims, offset);
			H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, values);
		}
		_logger.LogTrace("{Dataset} read from {StartRow} ({RowCount} rows).", datasetName, startRow, numRows);
	}

	public T[,] ReadRows<T>(string datasetName, int startRow, int numRows) where T : unmanaged
	{
		T[,] values = null;
		ReadRows(datasetName, startRow, numRows, ref values);
		return values;
	}

	public T[,] ReadRows<T>(H5DatasetID d, int startRow, int numRows) where T : unmanaged
	{
		T[,] values = null;
		ReadRows(d, startRow, numRows, ref values);
		return values;
	}

	public H5DatasetID OpenDataset(string datasetName, int cacheSizeInBytes = 0)
	{
		return H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true, needCreatePropertyList: true, cacheSizeInBytes);
	}

	public H5DatasetID TryOpenDataset(string datasetName)
	{
		if (!ExistsAsDataset(datasetName))
		{
			return null;
		}
		if (H5DatasetID.TryOpen(FileId, datasetName, out var dset))
		{
			return dset;
		}
		return null;
	}

	public void ReadRows<T>(H5DatasetID dataset, int startRow, int numRows, ref T[,] values) where T : unmanaged
	{
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataspaceID);
		RowColArgCheck(startRow, numRows, 0, (int)dimensions[1], dimensions, dataset.DatasetName, "ReadRows");
		Hyperslab.GetRowDims(startRow, numRows, dimensions, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, hyperslabDims);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, hyperslabDims, offset);
		H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, values);
	}

	public void ReadRows<T>(H5DatasetID dataset, int startRow, int numRows, Memory<T> buffer) where T : unmanaged
	{
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataspaceID);
		if (dimensions.Length != 2)
		{
			throw new ArgumentException("Not a 2D dataset");
		}
		RowColArgCheck(startRow, numRows, 0, (int)dimensions[1], dimensions, dataset.DatasetName, "ReadRows");
		Hyperslab.GetRowDims(startRow, numRows, dimensions, out var hyperslabDims, out var offset);
		if (buffer.Length < (int)(hyperslabDims[0] * hyperslabDims[1]))
		{
			throw new ArgumentException("Buffer length incorrect.");
		}
		using (H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, hyperslabDims, offset))
		{
			H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, buffer);
		}
		_logger.LogTrace("{Dataset} read from {StartRow} ({RowCount} rows).", dataset.DatasetName, startRow, numRows);
	}

	public void ReadRow<T>(string datasetName, int rowIdx, ref T[] values) where T : unmanaged
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ReadRow(dataset, rowIdx, ref values);
	}

	public void ReadRow<T>(H5DatasetID dataset, int rowIdx, ref T[] values) where T : unmanaged
	{
		DatasetIntegrityCheck<T>(dataset, 2, "ReadRow");
		long datasetId = dataset.DatasetId;
		long typeId = dataset.DataType.TypeId;
		long spaceId = dataset.SpaceId;
		ulong[] dimensions = GetDimensions(spaceId);
		RowColArgCheck(rowIdx, 1, 0, (int)dimensions[1], dimensions, dataset.DatasetName, "ReadRow");
		Hyperslab.GetRowDims(rowIdx, 1, dimensions, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, new ulong[1] { hyperslabDims[1] });
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(spaceId, hyperslabDims, offset);
		H5DReadInternal<T>(datasetId, typeId, spaceId, h5HyperslabID.SpaceId, values);
	}

	public void ReadRow<T>(H5DatasetID dataset, int rowIdx, Memory<T> buffer) where T : unmanaged
	{
		DatasetIntegrityCheck<T>(dataset, 2, "ReadRow");
		long datasetId = dataset.DatasetId;
		long typeId = dataset.DataType.TypeId;
		long spaceId = dataset.SpaceId;
		ulong[] dimensions = GetDimensions(spaceId);
		RowColArgCheck(rowIdx, 1, 0, (int)dimensions[1], dimensions, dataset.DatasetName, "ReadRow");
		Hyperslab.GetRowDims(rowIdx, 1, dimensions, out var hyperslabDims, out var offset);
		if (buffer.Length != (int)hyperslabDims[1])
		{
			throw new ArgumentException("Buffer incorrect size.");
		}
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(spaceId, hyperslabDims, offset);
		H5DReadInternal(datasetId, typeId, spaceId, h5HyperslabID.SpaceId, buffer);
	}

	public void ReadPartialRow<T>(H5DatasetID dataset, int rowIdx, int colStart, Memory<T> buffer) where T : unmanaged
	{
		int length = buffer.Length;
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(hyperslabDims: new ulong[2]
		{
			1uL,
			(ulong)length
		}, offset: new ulong[2]
		{
			(ulong)rowIdx,
			(ulong)colStart
		}, spaceId: dataset.SpaceId);
		if (H5DReadInternal(dataset.DatasetId, dataset.DataType.TypeId, dataset.SpaceId, h5HyperslabID.SpaceId, buffer) < 0)
		{
			_logger.LogWarning("Error in ReadPartialRow while reading dataset {Dataset}", dataset.DatasetName);
		}
	}

	public void ReadStringRows(string datasetName, int startRow, int numRows, ref string[,] values)
	{
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<string>(h5DatasetID, 2, "ReadStringRows");
		ulong[] dimensions = GetDimensions(h5DatasetID.SpaceId);
		RowColArgCheck(startRow, numRows, 0, (int)dimensions[1], dimensions, datasetName, "ReadStringRows");
		Hyperslab.GetRowDims(startRow, numRows, dimensions, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, hyperslabDims);
		byte[,] byteValues = null;
		int get_size = (int)H5T.get_size(h5DatasetID.DataType.TypeId);
		using (H5HyperslabID h5HyperslabID = H5HyperslabID.Open(h5DatasetID.SpaceId, hyperslabDims, offset))
		{
			byteValues = new byte[(uint)hyperslabDims[0], (int)hyperslabDims[1] * get_size];
			H5DReadInternal(h5DatasetID.DatasetId, h5DatasetID.DataType.TypeId, h5DatasetID.SpaceId, h5HyperslabID.SpaceId, byteValues);
		}
		ParseString(byteValues, get_size, values);
	}

	public void ReadColumn<T>(string datasetName, int colIdx, ref T[] values) where T : unmanaged
	{
		T[,] values2 = null;
		ReadColumn(datasetName, colIdx, ref values2);
		if (values == null || values.Length != values2.GetLength(1))
		{
			values = new T[values2.GetLength(0)];
		}
		int sizeOf = Marshal.SizeOf(typeof(T));
		Buffer.BlockCopy(values2, 0, values, 0, values.Length * sizeOf);
	}

	public void ReadColumn<T>(string datasetName, int colIdx, ref T[,] values) where T : unmanaged
	{
		ReadColumns(datasetName, colIdx, 1, ref values);
	}

	public void ReadColumn<T>(H5DatasetID dataset, int colIdx, ref T[] values) where T : unmanaged
	{
		T[,] values2 = null;
		ReadColumn(dataset, colIdx, ref values2);
		if (values == null || values.Length != values2.GetLength(1))
		{
			values = new T[values2.GetLength(0)];
		}
		int sizeOf = Marshal.SizeOf<T>();
		Buffer.BlockCopy(values2, 0, values, 0, values.Length * sizeOf);
	}

	public void ReadColumn<T>(H5DatasetID dataset, int colIdx, ref T[,] values) where T : unmanaged
	{
		ReadColumns(dataset, colIdx, 1, ref values);
	}

	public void ReadColumns<T>(string datasetName, int startCol, int numCols, ref T[,] values) where T : unmanaged
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ReadColumns(dataset, startCol, numCols, ref values);
	}

	public void ReadColumns<T>(H5DatasetID dataset, int startCol, int numCols, ref T[,] values) where T : unmanaged
	{
		DatasetIntegrityCheck<T>(dataset, 2, "ReadColumns");
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataspaceID);
		RowColArgCheck(0, (int)dimensions[0], startCol, numCols, dimensions, dataset.DatasetName, "ReadColumns");
		Hyperslab.GetColDims(startCol, numCols, dimensions, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, hyperslabDims);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, hyperslabDims, offset);
		H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, values);
	}

	public void ReadColumn<T>(string dataset, int colIdx, Memory<T> buffer) where T : unmanaged
	{
		ReadColumns(dataset, colIdx, 1, buffer);
	}

	public void ReadColumn<T>(H5DatasetID dataset, int colIdx, Memory<T> buffer) where T : unmanaged
	{
		ReadColumns(dataset, colIdx, 1, buffer);
	}

	public void ReadColumns<T>(string dataset, int startCol, int numCols, Memory<T> buffer) where T : unmanaged
	{
		using H5DatasetID dataset2 = OpenDataset(dataset);
		ReadColumns(dataset2, startCol, numCols, buffer);
	}

	public void ReadColumns<T>(H5DatasetID dataset, int startCol, int numCols, Memory<T> buffer) where T : unmanaged
	{
		DatasetIntegrityCheck<T>(dataset, 2, "ReadColumns");
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataspaceID);
		RowColArgCheck(0, (int)dimensions[0], startCol, numCols, dimensions, dataset.DatasetName, "ReadColumns");
		Hyperslab.GetColDims(startCol, numCols, dimensions, out var hyperslabDims, out var offset);
		if (buffer.Length != (int)(hyperslabDims[0] * hyperslabDims[1]))
		{
			throw new ArgumentException("Buffer length doesn't match read dimensions.");
		}
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, hyperslabDims, offset);
		H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, buffer);
	}

	public void ReadColumns<T>(string datasetName, List<int> startColumns, List<int> numColumns, ref T[,] values) where T : unmanaged
	{
		if (startColumns == null || numColumns == null || startColumns.Count != numColumns.Count)
		{
			throw new ArgumentException("StartColumns/NumColumns are invalid.");
		}
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<T>(h5DatasetID, 2, "ReadColumns");
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataspaceID);
		for (int i = 0; i < startColumns.Count; i++)
		{
			int startCol = startColumns[i];
			int numCols = numColumns[i];
			RowColArgCheck(0, (int)dimensions[0], startCol, numCols, dimensions, datasetName, "ReadColumns");
		}
		using H5HyperslabID h5HyperslabID = H5HyperslabID.CreateComplexColumns(startColumns, numColumns, dimensions, dataspaceID);
		RedimIfNecessary(ref values, h5HyperslabID.Dimensions);
		H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, values);
	}

	public void ReadColumnsBatched<T>(string datasetName, ReadOnlySpan<int> columnIndexes, Memory<T>[] destinations) where T : unmanaged
	{
		using H5DatasetID dataset = OpenDataset(datasetName);
		ReadColumnsBatched(dataset, columnIndexes, destinations);
	}

	public void ReadColumnsBatched<T>(H5DatasetID dataset, ReadOnlySpan<int> columnIndexes, Memory<T>[] destinations) where T : unmanaged
	{
		if (columnIndexes.Length != destinations.Length)
		{
			throw new ArgumentException("Column index count must match destination buffer count.");
		}
		if (columnIndexes.Length == 0)
		{
			return;
		}
		DatasetIntegrityCheck<T>(dataset, 2, "ReadColumnsBatched");
		int datasetDimension = (int)GetDatasetDimensions(dataset)[0];
		for (int i = 0; i < destinations.Length; i++)
		{
			if (destinations[i].Length != datasetDimension)
			{
				throw new ArgumentException($"Destination buffer {i} must hold exactly {datasetDimension} values (one full column).");
			}
		}
		long[] cachedChunkSize = GetCachedChunkSize(dataset);
		if (cachedChunkSize.Length != 2)
		{
			for (int j = 0; j < columnIndexes.Length; j++)
			{
				ReadColumn(dataset, columnIndexes[j], destinations[j]);
			}
			return;
		}
		int cachedChunkSize2 = (int)cachedChunkSize[0];
		int cachedChunkSize3 = (int)cachedChunkSize[1];
		int count = (datasetDimension + cachedChunkSize2 - 1) / cachedChunkSize2;
		ColumnReadOrder columnReadOrder = ColumnReadOrder.Create(columnIndexes);
		int length = columnIndexes.Length;
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(cachedChunkSize2 * cachedChunkSize3, out mem))
		{
			Span<T> memSpan = mem.Span;
			int k = 0;
			while (k < length)
			{
				int columnIndexOverCachedChunkSize3 = columnIndexes[columnReadOrder.OriginalIndex(k)] / cachedChunkSize3;
				int ordinal = k;
				for (; k < length && columnIndexes[columnReadOrder.OriginalIndex(k)] / cachedChunkSize3 == columnIndexOverCachedChunkSize3; k++)
				{
				}
				int count2 = k;
				for (int l = 0; l < count; l++)
				{
					ReadChunkDirect(dataset, new int[2] { l, columnIndexOverCachedChunkSize3 }, memSpan);
					int cachedChunkSize2Term = l * cachedChunkSize2;
					int count3 = Math.Min(cachedChunkSize2, datasetDimension - cachedChunkSize2Term);
					for (int m = ordinal; m < count2; m++)
					{
						int originalIndex = columnReadOrder.OriginalIndex(m);
						int columnIndexesOriginalIndexTerm = columnIndexes[originalIndex] - columnIndexOverCachedChunkSize3 * cachedChunkSize3;
						Span<T> destination = destinations[originalIndex].Span;
						for (int n = 0; n < count3; n++)
						{
							destination[cachedChunkSize2Term + n] = memSpan[n * cachedChunkSize3 + columnIndexesOriginalIndexTerm];
						}
					}
				}
			}
		}
	}

	public void ReadElements<T>(string datasetName, int startElement, int numElements, ref T[] values) where T : unmanaged
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ReadElements(dataset, startElement, numElements, ref values);
	}

	public void ReadElements<T>(H5DatasetID dataset, int startElement, int numElements, ref T[] values) where T : unmanaged
	{
		DatasetIntegrityCheck<T>(dataset, 1, "ReadElements");
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataset.SpaceId);
		ElementArgCheck(startElement, numElements, dimensions, dataset.DatasetName, "ReadElements");
		Hyperslab.Get1DDims(startElement, numElements, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, hyperslabDims);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, hyperslabDims, offset);
		H5DReadInternal<T>(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, values);
	}

	public Dictionary<CompoundFieldDefinition, Array> ReadCompoundElements(string datasetName)
	{
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var _);
		ulong[] dimensions = GetDimensions(h5DatasetID.SpaceId);
		byte[] byteValues = new byte[h5DatasetID.DataType.GetSize() * (int)dimensions[0]];
		H5DReadInternal<byte>(datasetID, datatypeID, byteValues);
		return DebyteifyObjects(byteValues, h5DatasetID.DataType);
	}

	public Table ReadCompoundElementsAsTable(string datasetName)
	{
		Dictionary<CompoundFieldDefinition, Array> compoundElements = ReadCompoundElements(datasetName);
		Table table = new Table();
		foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement in compoundElements)
		{
			CompoundFieldDefinition key = compoundElement.Key;
			string name = key.Name;
			Type type = key.Type;
			table.AddColumn(name, type);
		}
		int length = compoundElements.First().Value.Length;
		for (int i = 0; i < length; i++)
		{
			table.AddNewRow();
		}
		foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement2 in compoundElements)
		{
			string name2 = compoundElement2.Key.Name;
			Array compoundElement2Term = compoundElement2.Value;
			int columnIndex = table.GetColumnIndex(name2);
			for (int j = 0; j < length; j++)
			{
				table.Rows[j][columnIndex] = compoundElement2Term.GetValue(j);
			}
		}
		return table;
	}

	public CompoundDataset ReadCompoundDataset(string datasetName)
	{
		return new CompoundDataset(ReadCompoundElements(datasetName));
	}

	public void MultiReadElements<T>(string datasetName, IList<int> startElements, IList<int> numElements, ref T[] values) where T : unmanaged
	{
		if (startElements == null || numElements == null || startElements.Count != numElements.Count)
		{
			throw new ArgumentException("StartElements/NumElements are invalid.");
		}
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<string>(h5DatasetID, 1, "MultiReadElements");
		ulong[] dimensions = GetDimensions(h5DatasetID.SpaceId);
		for (int i = 0; i < startElements.Count; i++)
		{
			int startElement = startElements[i];
			int numElements2 = numElements[i];
			ElementArgCheck(startElement, numElements2, dimensions, datasetName, "MultiReadElements");
		}
		using H5HyperslabID h5HyperslabID = H5HyperslabID.CreateComplexElements(numElements, startElements, dimensions, h5DatasetID.SpaceId);
		RedimIfNecessary(ref values, h5HyperslabID.Dimensions);
		H5DReadInternal<T>(h5DatasetID.DatasetId, h5DatasetID.DataType.TypeId, h5DatasetID.SpaceId, h5HyperslabID.SpaceId, values);
	}

	public void ReadJaggedLine<T>(string infoDataset, string valuesDataset, int element, ref T[] values) where T : unmanaged
	{
		int[,] values2 = null;
		ReadRow(infoDataset, element, ref values2);
		int startElement = values2[0, 0];
		int numElements = values2[0, 1];
		ReadElements(valuesDataset, startElement, numElements, ref values);
	}

	public void ReadJaggedLine<T>(string infoDataset, string valuesDataset, int element, ref T[,] values) where T : unmanaged
	{
		int[,] values2 = null;
		ReadRow(infoDataset, element, ref values2);
		int startRow = values2[0, 0];
		int numRows = values2[0, 1];
		ReadRows(valuesDataset, startRow, numRows, ref values);
	}

	public void ReadJagged1D<T>(string dataset, Action<T[]> nextElement) where T : unmanaged
	{
		using H5DatasetID dataset2 = OpenDataset(dataset);
		ReadAttribute(dataset, "Start", out int[] values);
		ReadAttribute(dataset, "Count", out int[] values2);
		int count = values.Length;
		for (int i = 0; i < count; i++)
		{
			T[] values3 = null;
			ReadElements(dataset2, values[i], values2[i], ref values3);
			nextElement(values3);
		}
	}

	public void ReadJagged1D<T>(string dataset, out T[] elements, out int[] starts, out int[] counts) where T : unmanaged
	{
		using H5DatasetID dataset2 = OpenDataset(dataset);
		ReadAttribute(dataset, "Start", out starts);
		ReadAttribute(dataset, "Count", out counts);
		ReadDataset(dataset2, out elements);
	}

	public void ReadJagged2D<T>(string dataset, Action<T[,]> nextElement) where T : unmanaged
	{
		using H5DatasetID dataset2 = OpenDataset(dataset);
		ReadAttribute(dataset, "Start", out int[] values);
		ReadAttribute(dataset, "Count", out int[] values2);
		int count = values.Length;
		for (int i = 0; i < count; i++)
		{
			T[,] values3 = null;
			ReadRows(dataset2, values[i], values2[i], ref values3);
			nextElement(values3);
		}
	}

	public void ReadJagged2D<T>(string dataset, out T[,] elements, out int[] starts, out int[] counts) where T : unmanaged
	{
		using H5DatasetID dataset2 = OpenDataset(dataset);
		ReadAttribute(dataset, "Start", out starts);
		ReadAttribute(dataset, "Count", out counts);
		ReadDataset(dataset2, out elements);
	}

	public void ReadJagged_Old<T>(string infoDataset, string valueDataset, Action<T[,]> nextElement) where T : unmanaged
	{
		if (!ExistsAsDataset(infoDataset))
		{
			throw new ArgumentException(infoDataset + " does not exist.");
		}
		if (!ExistsAsDataset(valueDataset))
		{
			throw new ArgumentException(infoDataset + " does not exist.");
		}
		ReadDataset(infoDataset, out int[,] values);
		int length = values.GetLength(0);
		using H5DatasetID dataset = OpenDataset(valueDataset);
		for (int i = 0; i < length; i++)
		{
			int startRow = values[i, 0];
			int numRows = values[i, 1];
			T[,] values2 = null;
			ReadRows(dataset, startRow, numRows, ref values2);
			nextElement(values2);
		}
	}

	public void ReadChunkDirect<T>(H5DatasetID dataset, int[] chunkIndex, ref T[] values) where T : unmanaged
	{
		T[] valTemp = values;
		ReadChunkDirectInternal<T>(dataset, chunkIndex, (Memory<byte> mem, long[] chunkDims) =>
		{
			RedimIfNecessary(ref valTemp, chunkDims);
			CompressionHelper.Decompress(mem.Span, valTemp);
		});
		values = valTemp;
	}

	public void ReadChunkDirect<T>(H5DatasetID dataset, int[] chunkIndex, Memory<T> values) where T : unmanaged
	{
		ReadDecompressChunkDirectInternal(dataset, chunkIndex, values.Span);
	}

	public void ReadChunkDirect<T>(H5DatasetID dataset, int[] chunkIndex, Span<T> values) where T : unmanaged
	{
		ReadDecompressChunkDirectInternal(dataset, chunkIndex, values);
	}

	public void ReadChunkDirect<T>(H5DatasetID dataset, int[] chunkIndex, ref T[,] values) where T : unmanaged
	{
		T[,] valTemp = values;
		ReadChunkDirectInternal<T>(dataset, chunkIndex, (Memory<byte> mem, long[] chunkDims) =>
		{
			RedimIfNecessary(ref valTemp, chunkDims);
			CompressionHelper.Decompress(mem.Span, valTemp);
		});
		values = valTemp;
	}

	private unsafe void ReadChunkPrecheck(H5DatasetID dataset, int[] chunkIndex, out long[] chunkDims, out uint filter_mask, out ulong[] elementIndex, out ulong compSizeBytes)
	{
		filter_mask = 1u;
		chunkDims = GetCachedChunkSize(dataset);
		elementIndex = new ulong[chunkIndex.Length];
		for (int i = 0; i < chunkIndex.Length; i++)
		{
			elementIndex[i] = (ulong)(chunkIndex[i] * chunkDims[i]);
		}
		compSizeBytes = 0uL;
		fixed (ulong* offset = elementIndex)
		{
			H5D.get_chunk_storage_size(dataset.DatasetId, offset, ref compSizeBytes);
		}
	}

	private unsafe void ReadChunkDirectInternal<T>(H5DatasetID dataset, int[] chunkIndex, Action<Memory<byte>, long[]> useChunk)
	{
		ReadChunkPrecheck(dataset, chunkIndex, out var chunkDims, out var filter_mask, out var elementIndex, out var compSizeBytes);
		Memory<byte> mem;
		using (SharedMemoryPool<byte>.Rent((int)compSizeBytes, out mem))
		{
			fixed (byte* buf = mem.Span)
			{
				Advanced.read_chunk(dataset.DatasetId, 0L, elementIndex, ref filter_mask, (nint)buf);
			}
			useChunk(mem, chunkDims);
		}
	}

	private unsafe void ReadChunkDirectInternal<TData, TParam>(H5DatasetID dataset, int[] chunkIndex, TParam payload, Action<Memory<byte>, long[], TParam> useChunk)
	{
		ReadChunkPrecheck(dataset, chunkIndex, out var chunkDims, out var filter_mask, out var elementIndex, out var compSizeBytes);
		Memory<byte> mem;
		using (SharedMemoryPool<byte>.Rent((int)compSizeBytes, out mem))
		{
			fixed (byte* buf = mem.Span)
			{
				Advanced.read_chunk(dataset.DatasetId, 0L, elementIndex, ref filter_mask, (nint)buf);
			}
			useChunk(mem, chunkDims, payload);
		}
	}

	private unsafe void ReadDecompressChunkDirectInternal<T>(H5DatasetID dataset, int[] chunkIndex, Span<T> data) where T : unmanaged
	{
		ReadChunkPrecheck(dataset, chunkIndex, out var _, out var filter_mask, out var elementIndex, out var compSizeBytes);
		Memory<byte> mem;
		using (SharedMemoryPool<byte>.Rent((int)compSizeBytes, out mem))
		{
			fixed (byte* buf = mem.Span)
			{
				Advanced.read_chunk(dataset.DatasetId, 0L, elementIndex, ref filter_mask, (nint)buf);
			}
			CompressionHelper.Decompress(mem.Span, data);
		}
	}

	public void ReadCompressedChunk(H5DatasetID dataset, int[] chunkIndex, Action<ReadOnlyMemory<byte>> useChunk)
	{
		ReadChunkDirectInternal<byte>(dataset, chunkIndex, (Memory<byte> mem, long[] _) =>
		{
			useChunk(mem);
		});
	}

	public void StreamDatasetBytes(H5DatasetID dataset, Action<ReadOnlyMemory<byte>> useBytes)
	{
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataspaceID);
		int get_size = (int)H5T.get_size(datatypeID);
		long unity = 1L;
		for (int i = 0; i < dimensions.Length; i++)
		{
			unity *= (long)dimensions[i];
		}
		if (dimensions.Length == 0 || unity == 0L)
		{
			StreamDatasetBytesWhole(datasetID, datatypeID, unity * get_size, useBytes);
			return;
		}
		long unity2 = 1L;
		for (int j = 1; j < dimensions.Length; j++)
		{
			unity2 *= (long)dimensions[j];
		}
		long get_sizeTerm = unity2 * get_size;
		long[] cachedChunkSize = GetCachedChunkSize(dataset);
		long cachedChunkSize2Term;
		if (cachedChunkSize.Length == dimensions.Length && cachedChunkSize[0] > 0)
		{
			long cachedChunkSize2 = cachedChunkSize[0];
			long cachedChunkSize2Term2 = Math.Max(1L, 8388608 / Math.Max(1L, cachedChunkSize2 * get_sizeTerm));
			cachedChunkSize2Term = cachedChunkSize2 * cachedChunkSize2Term2;
		}
		else
		{
			cachedChunkSize2Term = Math.Max(1L, 8388608 / Math.Max(1L, get_sizeTerm));
		}
		cachedChunkSize2Term = Math.Min(cachedChunkSize2Term, (long)dimensions[0]);
		for (long ulongsArray2 = 0L; ulongsArray2 < (long)dimensions[0]; ulongsArray2 += cachedChunkSize2Term)
		{
			long ulongs2 = Math.Min(cachedChunkSize2Term, (long)dimensions[0] - ulongsArray2);
			ulong[] ulongs = (ulong[])dimensions.Clone();
			ulongs[0] = (ulong)ulongs2;
			ulong[] ulongsArray = new ulong[dimensions.Length];
			ulongsArray[0] = (ulong)ulongsArray2;
			Memory<byte> mem;
			using (SharedMemoryPool<byte>.Rent((int)(ulongs2 * get_sizeTerm), out mem))
			{
				using (H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, ulongs, ulongsArray))
				{
					H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, mem);
				}
				useBytes(mem);
			}
		}
	}

	private unsafe void StreamDatasetBytesWhole(long datasetId, long typeId, long totalBytes, Action<ReadOnlyMemory<byte>> useBytes)
	{
		if (totalBytes <= 0)
		{
			useBytes(ReadOnlyMemory<byte>.Empty);
			return;
		}
		Memory<byte> mem;
		using (SharedMemoryPool<byte>.Rent((int)totalBytes, out mem))
		{
			using (MemoryHandle memoryHandle = mem.Pin())
			{
				H5D.read(datasetId, typeId, 0L, 0L, 0L, (nint)memoryHandle.Pointer);
			}
			useBytes(mem);
		}
	}

	public long CompressedSize(string dsetName)
	{
		long chunk_bytesSum = 0L;
		long[] chunkSize = GetChunkSize(dsetName);
		long[] datasetDimensions = GetDatasetDimensions(dsetName);
		int datasetDimensionLength = datasetDimensions.Length;
		long[] longs = new long[chunkSize.Length];
		for (int i = 0; i < chunkSize.Length; i++)
		{
			longs[i] = (long)Math.Ceiling((double)datasetDimensions[i] / (double)chunkSize[i]);
		}
		long[] longsArray = new long[chunkSize.Length];
		ulong[] ulongs = new ulong[chunkSize.Length];
		long unity = 1L;
		for (int j = 0; j < datasetDimensionLength; j++)
		{
			unity *= longs[j];
		}
		using H5DatasetID h5DatasetID = OpenDataset(dsetName);
		ulong chunk_bytes = 0uL;
		H5D.get_chunk_storage_size(h5DatasetID.DatasetId, ulongs, ref chunk_bytes);
		chunk_bytesSum += (long)chunk_bytes;
		while (true)
		{
			bool condition = false;
			int lengthOffset = longsArray.Length - 1;
			while (lengthOffset >= 0)
			{
				if (longsArray[lengthOffset] == longs[lengthOffset] - 1)
				{
					if (lengthOffset == 0)
					{
						condition = true;
						break;
					}
					lengthOffset--;
					continue;
				}
				longsArray[lengthOffset]++;
				for (int k = lengthOffset + 1; k < longsArray.Length; k++)
				{
					longsArray[k] = 0L;
				}
				break;
			}
			if (condition)
			{
				break;
			}
			for (int l = 0; l < longsArray.Length; l++)
			{
				ulongs[l] = (ulong)(longsArray[l] * chunkSize[l]);
			}
			H5D.get_chunk_storage_size(h5DatasetID.DatasetId, ulongs, ref chunk_bytes);
			chunk_bytesSum += (long)chunk_bytes;
		}
		return chunk_bytesSum;
	}

	public string HashDataset(string datasetPath, H5HashAlgorithm algorithm = H5HashAlgorithm.SHA256, bool compressed = true)
	{
		int rows;
		int cols;
		return H5Hasher.HashDataset(this, datasetPath, out rows, out cols, algorithm, compressed);
	}

	public string HashDataset(string datasetPath, out int rows, out int cols, H5HashAlgorithm algorithm = H5HashAlgorithm.SHA256, bool compressed = true)
	{
		return H5Hasher.HashDataset(this, datasetPath, out rows, out cols, algorithm, compressed);
	}

	private static Dictionary<CompoundFieldDefinition, Array> DebyteifyObjects(byte[] bytes, H5DataTypeID datatype)
	{
		Dictionary<CompoundFieldDefinition, Array> valuesByCompoundFieldDefinition = new Dictionary<CompoundFieldDefinition, Array>();
		CompoundTypeDefinition compoundDataType = datatype.GetCompoundDataType();
		int size = datatype.GetSize();
		if (bytes.Length % size != 0)
		{
			throw new ArgumentException("Incorrect compound buffer size");
		}
		Encoding aSCII = Encoding.UTF8;
		int count = (int)Math.Round((double)bytes.Length / (double)size);
		foreach (CompoundFieldDefinition field in compoundDataType.Fields)
		{
			Type type = field.Type;
			Array values = null;
			if (type == typeof(byte))
			{
				values = new byte[count];
			}
			else if (type == typeof(bool))
			{
				values = new bool[count];
			}
			else if (type == typeof(short))
			{
				values = new short[count];
			}
			else if (type == typeof(int))
			{
				values = new int[count];
			}
			else if (type == typeof(long))
			{
				values = new long[count];
			}
			else if (type == typeof(float))
			{
				values = new float[count];
			}
			else if (type == typeof(double))
			{
				values = new double[count];
			}
			else if (type == typeof(string))
			{
				values = new string[count];
			}
			int offset = field.Offset;
			int size2 = field.Size;
			for (int i = 0; i < count; i++)
			{
				if (type == typeof(byte))
				{
					((byte[])values)[i] = bytes[offset];
				}
				else if (type == typeof(bool))
				{
					((bool[])values)[i] = BitConverter.ToBoolean(bytes, offset);
				}
				else if (type == typeof(short))
				{
					((short[])values)[i] = BitConverter.ToInt16(bytes, offset);
				}
				else if (type == typeof(int))
				{
					((int[])values)[i] = BitConverter.ToInt32(bytes, offset);
				}
				else if (type == typeof(long))
				{
					((long[])values)[i] = BitConverter.ToInt64(bytes, offset);
				}
				else if (type == typeof(float))
				{
					((float[])values)[i] = BitConverter.ToSingle(bytes, offset);
				}
				else if (type == typeof(double))
				{
					((double[])values)[i] = BitConverter.ToDouble(bytes, offset);
				}
				else if (type == typeof(string))
				{
					int intervalIndex = 0;
					for (intervalIndex = 0; intervalIndex < size2 && bytes[offset + intervalIndex] != 0; intervalIndex++)
					{
					}
					((string[])values)[i] = aSCII.GetString(bytes, offset, intervalIndex);
				}
				offset += size;
			}
			valuesByCompoundFieldDefinition[field] = values;
		}
		return valuesByCompoundFieldDefinition;
	}

	private void ParseString(byte[] byteValues, int strLength, string[] values)
	{
		Encoding aSCII = Encoding.UTF8;
		int count = byteValues.Length / strLength;
		if (count != values.Length)
		{
			throw new ArgumentException("Buffer size mismatch for ParseString");
		}
		for (int i = 0; i < count; i++)
		{
			Span<byte> slice = byteValues.AsSpan().Slice(i * strLength, strLength);
			int indexOf = System.MemoryExtensions.IndexOf((ReadOnlySpan<byte>)slice, (byte)0);
			if (indexOf == -1)
			{
				values[i] = aSCII.GetString(slice);
			}
			else
			{
				values[i] = aSCII.GetString(slice.Slice(0, indexOf));
			}
		}
	}

	private void ParseString(byte[,] byteValues, int strLength, string[,] values)
	{
		Encoding aSCII = Encoding.UTF8;
		int count = byteValues.GetLength(1) / strLength;
		int length = byteValues.GetLength(0);
		for (int i = 0; i < length; i++)
		{
			for (int j = 0; j < count; j++)
			{
				Span<byte> slice = byteValues.AsSpan().Slice(i * count * strLength + j * strLength, strLength);
				int indexOf = System.MemoryExtensions.IndexOf((ReadOnlySpan<byte>)slice, (byte)0);
				if (indexOf == -1)
				{
					values[i, j] = aSCII.GetString(slice);
				}
				else
				{
					values[i, j] = aSCII.GetString(slice.Slice(0, indexOf));
				}
			}
		}
	}

	protected static Type GetAttributeType(long objId, string attrName)
	{
		long attr_id = H5A.open_by_name(objId, CURRENT_LOCATION, attrName, 0L, 0L);
		long get_type = H5A.get_type(attr_id);
		long space_id = H5A.get_space(attr_id);
		int ndims = H5S.get_simple_extent_ndims(space_id);
		Type result = H5DataTypeID.TypeConvert(get_type, ndims);
		H5A.close(attr_id);
		H5T.close(get_type);
		H5S.close(space_id);
		return result;
	}

	protected unsafe static void GetDimensions(long spaceId, out ulong nElements)
	{
		if (H5S.get_simple_extent_ndims(spaceId) != 1)
		{
			throw new ArgumentException("Dataset was not 1D");
		}
		ulong nElements2 = 0uL;
		H5S.get_simple_extent_dims(spaceId, &nElements2, null);
		nElements = nElements2;
	}

	protected unsafe static void GetDimensions(long spaceId, out ulong nRows, out ulong nCols)
	{
		if (H5S.get_simple_extent_ndims(spaceId) != 2)
		{
			throw new ArgumentException("Dataset was not 2D");
		}
		ulong* localValue = stackalloc ulong[2];
		H5S.get_simple_extent_dims(spaceId, localValue, null);
		nRows = *localValue;
		nCols = localValue[1];
	}

	protected unsafe static void GetDimensions(long spaceId, out ulong nPlanes, out ulong nRows, out ulong nCols)
	{
		if (H5S.get_simple_extent_ndims(spaceId) != 3)
		{
			throw new ArgumentException("Dataset was not 3D");
		}
		ulong* localValue = stackalloc ulong[3];
		H5S.get_simple_extent_dims(spaceId, localValue, null);
		nPlanes = *localValue;
		nRows = localValue[1];
		nCols = localValue[2];
	}

	protected ulong[] GetDimensions(long spaceId)
	{
		ulong[] ulongs = new ulong[H5S.get_simple_extent_ndims(spaceId)];
		H5S.get_simple_extent_dims(spaceId, ulongs, null);
		return ulongs;
	}

	protected void RedimIfNecessary<T>(ref T[] values, ulong[] size)
	{
		if (size.GetLength(0) != 1)
		{
			throw new Exception("Requested array is dimensioned incorrectly.");
		}
		if (values == null || values.GetLength(0) != (int)size[0])
		{
			values = new T[Convert.ToInt32(size[0])];
		}
	}

	protected void RedimIfNecessary<T>(ref T[] values, long[] size)
	{
		if (size.Length == 1)
		{
			if (values == null || values.GetLength(0) != (int)size[0])
			{
				values = new T[(int)size[0]];
			}
			return;
		}
		if (size.Length == 2)
		{
			if (values == null || values.Length != (int)(size[0] * size[1]))
			{
				values = new T[(int)(size[0] * size[1])];
			}
			return;
		}
		throw new NotSupportedException();
	}

	protected void RedimIfNecessary<T>(ref T[,] values, ulong[] size)
	{
		if (size.GetLength(0) != 2)
		{
			throw new Exception("Requested array is dimensioned incorrectly.");
		}
		if (values == null || values.GetLength(0) != (int)size[0] || values.GetLength(1) != (int)size[1])
		{
			values = new T[Convert.ToInt32(size[0]), Convert.ToInt32(size[1])];
		}
	}

	protected void RedimIfNecessary<T>(ref T[,] values, long[] size)
	{
		if (size.Length != 2)
		{
			throw new Exception("Requested array is dimensioned incorrectly.");
		}
		if (values == null || values.GetLength(0) != (int)size[0] || values.GetLength(1) != (int)size[1])
		{
			values = new T[Convert.ToInt32(size[0]), Convert.ToInt32(size[1])];
		}
	}

	protected void RedimIfNecessary<T>(ref T[,,] values, ulong[] size)
	{
		if (size.Length != 3)
		{
			throw new Exception("Requested array is dimensioned incorrectly.");
		}
		if (values == null || values.GetLength(0) != (int)size[0] || values.GetLength(1) != (int)size[1] || values.GetLength(2) != (int)size[2])
		{
			values = new T[(uint)size[0], (uint)size[1], (uint)size[2]];
		}
	}

	protected void RedimIfNecessary<T>(ref T[,,] values, long[] size)
	{
		if (size.GetLength(0) != 3)
		{
			throw new Exception("Requested array is dimensioned incorrectly.");
		}
		if (values == null || values.GetLength(0) != (int)size[0] || values.GetLength(1) != (int)size[1] || values.GetLength(2) != (int)size[2])
		{
			values = new T[(int)size[0], (int)size[1], (int)size[2]];
		}
	}

	private ulong[] EnsureFullSize<T>(long spaceId, ref T[] values)
	{
		ulong[] ulongs = new ulong[1];
		H5S.get_simple_extent_dims(spaceId, ulongs, null);
		RedimIfNecessary(ref values, ulongs);
		return ulongs;
	}

	private ulong[] EnsureFullSize<T>(long spaceId, ref T[,] values)
	{
		ulong[] ulongs = new ulong[2];
		H5S.get_simple_extent_dims(spaceId, ulongs, null);
		RedimIfNecessary(ref values, ulongs);
		return ulongs;
	}

	private ulong[] EnsureFullSize<T>(long spaceId, ref T[,,] values)
	{
		ulong[] ulongs = new ulong[3];
		H5S.get_simple_extent_dims(spaceId, ulongs, null);
		RedimIfNecessary(ref values, ulongs);
		return ulongs;
	}

	protected void EnsureAttributeExists(string objectName, string attributeName, [CallerMemberName] string functionName = "")
	{
		if (!ObjectExists(objectName))
		{
			throw new ObjectNotFoundException(objectName, functionName);
		}
		if (!ExistsAsAttribute(objectName, attributeName))
		{
			throw new AttributeNotFoundException(objectName, attributeName, functionName);
		}
	}

	protected void EnsureDatasetExists(string objectName, [CallerMemberName] string functionName = "")
	{
		if (!ExistsAsDataset(objectName))
		{
			throw new ObjectNotFoundException(objectName, functionName);
		}
	}

	protected void EnsureGroupExists(string objectName, [CallerMemberName] string functionName = "")
	{
		if (objectName == "/" || ExistsAsGroup(objectName))
		{
			return;
		}
		throw new ObjectNotFoundException(objectName, functionName);
	}

	protected void EnsureObjectExists(string objectName, [CallerMemberName] string functionName = "")
	{
		if (objectName == "/" || ObjectExists(objectName))
		{
			return;
		}
		throw new ObjectNotFoundException(objectName, functionName);
	}

	protected static void StripEmpty(ref string[] newStrings)
	{
		string[] newStringsArray = newStrings;
		int count = 0;
		string[] newStrings2 = new string[newStringsArray.Length + 1];
		for (int i = 0; i <= newStringsArray.Length - 1; i++)
		{
			if (!string.IsNullOrEmpty(newStringsArray[i]))
			{
				newStrings2[count] = newStringsArray[i];
				count++;
			}
		}
		if (newStrings == null || newStrings.Length != count)
		{
			newStrings = new string[count];
		}
		Array.Copy(newStrings2, newStrings, count);
	}

	public bool TryReadMessageLog(string datasetPrefix, out IReadOnlyList<MessageLogEntry> entries)
	{
		entries = null;
		if (string.IsNullOrEmpty(datasetPrefix))
		{
			return false;
		}
		string datasetPrefixTerm = datasetPrefix + " (Structure)";
		if (!ExistsAsDataset(datasetPrefixTerm))
		{
			return false;
		}
		Table table = ReadCompoundElementsAsTable(datasetPrefixTerm);
		int count = table.Rows.Count;
		if (count == 0)
		{
			return false;
		}
		string[] tryReadMessageLogStrings = TryReadMessageLogStrings(datasetPrefix + " (Data)");
		MessageLogEntry[] entriesArray = new MessageLogEntry[count];
		int ordinal = 0;
		for (int i = 0; i < count; i++)
		{
			TableRow row = table.Rows[i];
			DateTime timestamp = TicksToDateTime(ToLong(GetCell(table, row, "Time (Ticks)")));
			MessageSeverity severity = (MessageSeverity)ToInt(GetCell(table, row, "Severity"));
			string code = (GetCell(table, row, "Code") as string) ?? "";
			int count2 = Math.Max(0, ToInt(GetCell(table, row, "Data Length")));
			string[] strings = new string[count2];
			for (int j = 0; j < count2; j++)
			{
				strings[j] = ((tryReadMessageLogStrings != null && ordinal < tryReadMessageLogStrings.Length) ? tryReadMessageLogStrings[ordinal] : "");
				ordinal++;
			}
			entriesArray[i] = new MessageLogEntry(timestamp, severity, code, strings);
		}
		entries = entriesArray;
		return true;
	}

	private string[] TryReadMessageLogStrings(string path)
	{
		if (!ExistsAsDataset(path))
		{
			return null;
		}
		ReadDataset(path, out var values);
		return values;
	}

	private static object GetCell(Table table, TableRow row, string column)
	{
		if (!table.TryGetColumnIndex(column, out var idx))
		{
			return null;
		}
		return row[idx];
	}

	private static long ToLong(object localValue)
	{
		if (localValue is long)
		{
			return (long)localValue;
		}
		if (localValue is int ordinal)
		{
			return ordinal;
		}
		return 0L;
	}

	private static int ToInt(object localValue)
	{
		if (localValue is int)
		{
			return (int)localValue;
		}
		if (localValue is long ordinal)
		{
			return (int)ordinal;
		}
		return 0;
	}

	private static DateTime TicksToDateTime(long ticks)
	{
		if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
		{
			return DateTime.MinValue;
		}
		return new DateTime(ticks);
	}

	public static string PathCombine(string initPath, params string[] appendPaths)
	{
		if (string.IsNullOrEmpty(initPath))
		{
			throw new ArgumentNullException("initPath");
		}
		if (appendPaths.Length == 0)
		{
			return initPath;
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(initPath);
		foreach (string appendPath in appendPaths)
		{
			if (string.IsNullOrEmpty(appendPath))
			{
				throw new ArgumentException("appendPaths");
			}
			char stringBuilder2 = stringBuilder[stringBuilder.Length - 1];
			if (!PathSeparatorChars.Contains(stringBuilder2))
			{
				stringBuilder.Append("/");
			}
			stringBuilder.Append(appendPath);
		}
		return stringBuilder.ToString();
	}

	public static string SanitizeObjectName(string name)
	{
		if (!name.Any((char c) => IllegalNameChars.Contains(c)))
		{
			return name;
		}
		char[] illegalNameChars = IllegalNameChars;
		foreach (char oldChar in illegalNameChars)
		{
			name = name.Replace(oldChar, '_');
		}
		return name;
	}

	public void CopyTo(H5Writer writer, string sourceObject, string destinationObject)
	{
		if (writer.ExistsAsGroup(destinationObject))
		{
			if (destinationObject == "/")
			{
				writer.CleanGroup(destinationObject);
			}
			else
			{
				writer.Delete(destinationObject);
			}
		}
		else if (writer.ExistsAsDataset(destinationObject))
		{
			writer.Delete(destinationObject);
		}
		writer.EnsureParentGroup(destinationObject);
		if (sourceObject == "/" && destinationObject == "/")
		{
			foreach (string groupName in GetGroupNames("/"))
			{
				CopyTo(writer, groupName, groupName);
			}
			foreach (string datasetName in GetDatasetNames("/"))
			{
				CopyTo(writer, datasetName, datasetName);
			}
			CopyAttributesTo(writer, "/", "/");
		}
		else if (H5O.copy(FileId, sourceObject, writer.FileId, destinationObject, 0L, 0L) < 0)
		{
			if (!ObjectExists(sourceObject))
			{
				throw new Exception("Source object " + sourceObject.InDoubleQuotes() + " does not exist.");
			}
			throw new Exception("Could not copy H5 Group/Dataset: " + sourceObject.InDoubleQuotes() + " -> " + destinationObject.InDoubleQuotes());
		}
	}

	public void CopyAttributesTo(H5Writer writer, string sourceObject, string destinationObject)
	{
		foreach (KeyValuePair<string, Type> attributeNamesAndType in GetAttributeNamesAndTypes(sourceObject))
		{
			string key = attributeNamesAndType.Key;
			if (!(attributeNamesAndType.Value != null))
			{
				continue;
			}
			Type type = attributeNamesAndType.Value;
			int nDims = 0;
			if (type.IsArray)
			{
				nDims = type.GetArrayRank();
				type = type.GetElementType();
			}
			if (type == typeof(string))
			{
				TransferStringAttribute(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(int))
			{
				TransferAttribute<int>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(long))
			{
				TransferAttribute<long>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(float))
			{
				TransferAttribute<float>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(double))
			{
				TransferAttribute<double>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(byte))
			{
				TransferAttribute<byte>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(short))
			{
				TransferAttribute<short>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(bool))
			{
				TransferAttribute<bool>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			throw new NotImplementedException($"CopyAttributesTo does not support attribute type {type.Name} for {key} on {sourceObject}");
		}
	}

	protected static void TransferAttribute<T>(H5Reader h5r, H5Writer h5w, string fromObject, string toObject, string name, int nDims) where T : unmanaged
	{
		switch (nDims)
		{
		case 0:
		{
			h5r.ReadAttributeScalar(fromObject, name, out T localValue);
			h5w.WriteAttribute(toObject, name, localValue);
			break;
		}
		case 1:
		{
			h5r.ReadAttribute(fromObject, name, out T[] values2);
			h5w.WriteAttribute(toObject, name, values2);
			break;
		}
		case 2:
		{
			h5r.ReadAttribute(fromObject, name, out T[,] values);
			h5w.WriteAttribute(toObject, name, values);
			break;
		}
		default:
			throw new NotImplementedException();
		}
	}

	protected static void TransferStringAttribute(H5Reader h5r, H5Writer h5w, string fromObject, string toObject, string name, int nDims)
	{
		switch (nDims)
		{
		case 0:
		{
			h5r.ReadAttributeScalar(fromObject, name, out var textValue);
			h5w.WriteAttribute(toObject, name, textValue);
			break;
		}
		case 1:
		{
			h5r.ReadAttribute(fromObject, name, out string[] values);
			h5w.WriteAttribute(toObject, name, values);
			break;
		}
		default:
			_ = 2;
			throw new NotImplementedException();
		}
	}

	public void TransposeToColumnChunking<T>(string sourceDataset, H5Writer writer, string destDataset, int destChunkWidth = -1, ProgressReporter prog = null) where T : unmanaged
	{
		if (!ExistsAsDataset(sourceDataset))
		{
			throw new ObjectNotFoundException(sourceDataset, "TransposeToColumnChunking");
		}
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		object progressLock = new object();
		int maxDegreeOfParallelism = Environment.ProcessorCount * 8;
		int sizeOfT = Marshal.SizeOf<T>();
		long[] datasetDimensions = GetDatasetDimensions(sourceDataset);
		int srcHeight = (int)datasetDimensions[0];
		int srcWidth = (int)datasetDimensions[1];
		long[] chunkSize = GetChunkSize(sourceDataset);
		int srcChunkHeight = (int)chunkSize[0];
		int srcChunkWidth = (int)chunkSize[1];
		int srcChunksTall = (int)Math.Ceiling((double)srcHeight / (double)srcChunkHeight);
		Math.Ceiling((double)srcWidth / (double)srcChunkWidth);
		if (srcChunkWidth != srcWidth)
		{
			throw new Exception("Error - source chunks must be the entire dataset width at this time.");
		}
		int destChunkHeight = srcHeight;
		if (destChunkWidth < 0)
		{
			destChunkWidth = ColumnChunkSize(srcHeight, srcWidth, 1048576, sizeOfT);
		}
		_ = new long[2] { destChunkHeight, destChunkWidth };
		Math.Ceiling((double)srcHeight / (double)destChunkHeight);
		int destChunksWide = (int)Math.Ceiling((double)srcWidth / (double)destChunkWidth);
		prog.ReportProgress(0);
		if ((long)srcHeight * (long)srcWidth * sizeOfT <= 104857600)
		{
			ReadDataset(sourceDataset, out T[,] values);
			writer.Create2dExtendibleDataset<T>(destDataset, destChunkHeight, destChunkWidth);
			writer.AddColumns(destDataset, values);
			prog.ReportProgress(100);
			return;
		}
		int tmpChunkHeight = (int)Math.Ceiling(10485760.0 / (double)(sizeOfT * srcWidth));
		int tmpChunkWidth = (int)Math.Ceiling(10485760.0 / (double)(sizeOfT * srcHeight));
		int srcChunksPerTempChunk = (int)Math.Ceiling((double)tmpChunkHeight / (double)srcChunkHeight);
		int destChunksPerTempChunk = (int)Math.Ceiling((double)tmpChunkWidth / (double)destChunkWidth);
		tmpChunkHeight = srcChunkHeight * srcChunksPerTempChunk;
		tmpChunkWidth = destChunkWidth * destChunksPerTempChunk;
		int tmpChunksTall = (int)Math.Ceiling((double)srcHeight / (double)tmpChunkHeight);
		int tmpChunksWide = (int)Math.Ceiling((double)srcWidth / (double)tmpChunkWidth);
		string combinePathTerm = Path.Combine(Path.GetTempPath(), "HDF_TEMP", Path.GetRandomFileName()) + ".h5";
		string datasetName = "/TEMP";
		ParallelOptions parallelOptions = new ParallelOptions();
		parallelOptions.MaxDegreeOfParallelism = maxDegreeOfParallelism;
		try
		{
			H5Writer tempWriter = new H5Writer(combinePathTerm);
			try
			{
				long tmpWidth = tmpChunkWidth * tmpChunksWide;
				long tmpHeight = tmpChunkHeight * tmpChunksTall;
				H5DatasetID readDset = OpenDataset(sourceDataset);
				try
				{
					ThreadLocal<T[,]> tlReadBuffer = new ThreadLocal<T[,]>(() => new T[srcChunkHeight, srcChunkWidth]);
					ThreadLocal<T[,]> tlTransferBlockBuffer = new ThreadLocal<T[,]>(() => new T[tmpChunkHeight, srcWidth]);
					ThreadLocal<T[,]> tlWriteBuffer = new ThreadLocal<T[,]>(() => new T[tmpChunkHeight, tmpChunkWidth]);
					tempWriter.Create2dExtendibleDataset<T>(datasetName, tmpChunkHeight, tmpChunkWidth);
					H5DatasetID writeDset = tempWriter.OpenDataset(datasetName);
					try
					{
						tempWriter.SetExtent(writeDset, (ulong)tmpHeight, (ulong)tmpWidth);
						int transferBlocksTall = tmpChunksTall;
						int transferBlocksCompleted = 0;
						System.Threading.Tasks.Parallel.For(0, transferBlocksTall, parallelOptions, (int blockIdx) =>
						{
							T[,] values2 = tlReadBuffer.Value;
							T[,] tlTransferBlockBufferTerm = tlTransferBlockBuffer.Value;
							T[,] tlWriteBufferTerm = tlWriteBuffer.Value;
							int blockIdxTimesTmpChunkHeightOverSrcChunkHeight = blockIdx * tmpChunkHeight / srcChunkHeight;
							int count = Math.Min(blockIdxTimesTmpChunkHeightOverSrcChunkHeight + srcChunksPerTempChunk, srcChunksTall);
							int[] intValues = new int[2];
							int lengthTimesSizeOfT = values2.Length * sizeOfT;
							for (int i = blockIdxTimesTmpChunkHeightOverSrcChunkHeight; i < count; i++)
							{
								int ordinal = i - blockIdxTimesTmpChunkHeightOverSrcChunkHeight;
								intValues[0] = i;
								ReadChunkDirect(readDset, intValues, ref values2);
								if (i == srcChunksTall - 1)
								{
									int srcChunkHeightSrcChunksTallTerm = srcChunkHeight * srcChunksTall - srcHeight;
									_ = 0;
									Buffer.BlockCopy(values2, 0, tlTransferBlockBufferTerm, lengthTimesSizeOfT * ordinal, srcWidth * (srcChunkHeight - srcChunkHeightSrcChunksTallTerm) * sizeOfT);
								}
								else
								{
									Buffer.BlockCopy(values2, 0, tlTransferBlockBufferTerm, lengthTimesSizeOfT * ordinal, lengthTimesSizeOfT);
								}
							}
							int[] intValuesArray = new int[2] { blockIdx, 0 };
							for (int j = 0; j < tmpChunksWide; j++)
							{
								int tmpChunkWidthTerm = j * tmpChunkWidth;
								int tmpChunkWidthTimesSizeOfT = tmpChunkWidth * sizeOfT;
								if (j == tmpChunksWide - 1)
								{
									tmpChunkWidthTimesSizeOfT -= (int)((tmpWidth - srcWidth) * sizeOfT);
								}
								for (int k = 0; k < tmpChunkHeight; k++)
								{
									Buffer.BlockCopy(tlTransferBlockBufferTerm, (k * srcWidth + tmpChunkWidthTerm) * sizeOfT, tlWriteBufferTerm, k * tmpChunkWidth * sizeOfT, tmpChunkWidthTimesSizeOfT);
								}
								intValuesArray[1] = j;
								tempWriter.WriteChunkDirect(writeDset, intValuesArray, tlWriteBufferTerm);
							}
							lock (progressLock)
							{
								transferBlocksCompleted++;
								float transferBlocksCompletedOverTransferBlocksTall = (float)transferBlocksCompleted / (float)transferBlocksTall;
								int prog2 = Math.Min(50, (int)Math.Round(50f * transferBlocksCompletedOverTransferBlocksTall));
								prog.ReportProgress(prog2);
							}
						});
					}
					finally
					{
						if (writeDset != null)
						{
							((IDisposable)writeDset).Dispose();
						}
					}
				}
				finally
				{
					if (readDset != null)
					{
						((IDisposable)readDset).Dispose();
					}
				}
				H5DatasetID readDset2 = tempWriter.OpenDataset(datasetName);
				try
				{
					ThreadLocal<T[,]> tlReadBuffer2 = new ThreadLocal<T[,]>(() => new T[tmpChunkHeight, tmpChunkWidth]);
					ThreadLocal<T[,]> tlTransferBlockBuffer2 = new ThreadLocal<T[,]>(() => new T[srcHeight, tmpChunkWidth]);
					ThreadLocal<T[,]> tlWriteBuffer2 = new ThreadLocal<T[,]>(() => new T[destChunkHeight, destChunkWidth]);
					writer.Create2dExtendibleDataset<T>(destDataset, destChunkHeight, destChunkWidth);
					int transferBlocksCompleted2 = 0;
					H5DatasetID writeDset2 = writer.OpenDataset(destDataset);
					try
					{
						int transferBlocksWide = tmpChunksWide;
						writer.SetExtent(writeDset2, datasetDimensions);
						System.Threading.Tasks.Parallel.For(0, transferBlocksWide, parallelOptions, (int blockIdx) =>
						{
							T[,] values2 = tlReadBuffer2.Value;
							T[,] tlTransferBlockBuffer2Term = tlTransferBlockBuffer2.Value;
							T[,] tlWriteBuffer2Term = tlWriteBuffer2.Value;
							_ = values2.Length;
							_ = sizeOfT;
							int[] intValues2 = new int[2] { 0, blockIdx };
							for (int i = 0; i < tmpChunksTall; i++)
							{
								intValues2[0] = i;
								tempWriter.ReadChunkDirect(readDset2, intValues2, ref values2);
								int tmpChunkHeightTerm = i * tmpChunkHeight;
								int tmpChunkHeight2 = tmpChunkHeight;
								if (i == tmpChunksTall - 1)
								{
									tmpChunkHeight2 = tmpChunkHeight - (int)(tmpHeight - srcHeight);
								}
								for (int j = 0; j < tmpChunkHeight2; j++)
								{
									Buffer.BlockCopy(values2, j * tmpChunkWidth * sizeOfT, tlTransferBlockBuffer2Term, (tmpChunkHeightTerm + j) * tmpChunkWidth * sizeOfT, tmpChunkWidth * sizeOfT);
								}
							}
							int[] intValues3 = new int[2];
							int blockIdxTimesDestChunksPerTempChunk = blockIdx * destChunksPerTempChunk;
							int destChunkWidthTerm = blockIdxTimesDestChunksPerTempChunk * destChunkWidth;
							for (int k = 0; k < destChunksPerTempChunk; k++)
							{
								intValues3[1] = blockIdxTimesDestChunksPerTempChunk + k;
								if (intValues3[1] == destChunksWide)
								{
									break;
								}
								int destChunkWidthTerm2 = k * destChunkWidth;
								int countEnd = (k + 1) * destChunkWidth;
								int countEndTerm = destChunkWidthTerm + countEnd;
								int countStart = countEnd;
								if (countEndTerm > srcWidth)
								{
									countStart -= countEndTerm - srcWidth;
								}
								for (int l = 0; l < destChunkHeight; l++)
								{
									for (int m = destChunkWidthTerm2; m < countStart; m++)
									{
										tlWriteBuffer2Term[l, m - destChunkWidthTerm2] = tlTransferBlockBuffer2Term[l, m];
									}
									if (countStart < countEnd)
									{
										for (int n = countStart; n < countEnd; n++)
										{
											tlWriteBuffer2Term[l, n - destChunkWidthTerm2] = default;
										}
									}
								}
								writer.WriteChunkDirect(writeDset2, intValues3, tlWriteBuffer2Term);
							}
							lock (progressLock)
							{
								transferBlocksCompleted2++;
								float transferBlocksCompleted2OverTransferBlocksWide = (float)transferBlocksCompleted2 / (float)transferBlocksWide;
								int prog2 = Math.Min(100, 50 + (int)Math.Round(50f * transferBlocksCompleted2OverTransferBlocksWide));
								prog.ReportProgress(prog2);
							}
						});
					}
					finally
					{
						if (writeDset2 != null)
						{
							((IDisposable)writeDset2).Dispose();
						}
					}
				}
				finally
				{
					if (readDset2 != null)
					{
						((IDisposable)readDset2).Dispose();
					}
				}
			}
			finally
			{
				if (tempWriter != null)
				{
					((IDisposable)tempWriter).Dispose();
				}
			}
		}
		catch
		{
			throw;
		}
		finally
		{
			if (File.Exists(combinePathTerm))
			{
				File.Delete(combinePathTerm);
			}
		}
	}

	public void TransposeWithIntermediateFile<T>(NativeResourceWrapper<H5Writer> targetWriter, string sourceDataset, string targetDataset, LockHelper writeLocker, ProgressReporter pr = null) where T : unmanaged
	{
		TransposeWithIntermediateFile<T>(new NativeResourceWrapper<H5Reader>(this), targetWriter, sourceDataset, targetDataset, writeLocker, pr);
	}

	public unsafe static void TransposeWithIntermediateFile<T>(NativeResourceWrapper<H5Reader> sourceReader, NativeResourceWrapper<H5Writer> targetWriter, string sourceDataset, string targetDataset, LockHelper writeLocker = null, ProgressReporter pr = null) where T : unmanaged
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		sourceReader.UseResource((H5Reader hr) =>
		{
			long[] datasetDimensions = hr.GetDatasetDimensions(sourceDataset);
			long unity = 1L;
			for (int i = 0; i < datasetDimensions.Length; i++)
			{
				unity *= datasetDimensions[i];
			}
			int ordinal = sizeof(T);
			if (unity * ordinal <= 104857600)
			{
				if (writeLocker != null)
				{
					using (writeLocker.TryGetWriterLock("Writing Transposed Dataset"))
					{
						targetWriter.UseResource((H5Writer hw) =>
						{
							hr.TransposeToColumnChunking<T>(sourceDataset, hw, targetDataset, -1, pr);
						});
						return;
					}
				}
				targetWriter.UseResource((H5Writer hw) =>
				{
					hr.TransposeToColumnChunking<T>(sourceDataset, hw, targetDataset, -1, pr);
				});
				return;
			}
			string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".h5");
			try
			{
				H5Writer tmpHw = new H5Writer(path);
				try
				{
					hr.TransposeToColumnChunking<T>(sourceDataset, tmpHw, targetDataset, -1, pr);
					if (writeLocker != null)
					{
						using (writeLocker.TryGetWriterLock("Writing Transposed Dataset"))
						{
							targetWriter.UseResource((H5Writer hw) =>
							{
								tmpHw.CopyTo(hw, targetDataset, targetDataset);
							});
							return;
						}
					}
					targetWriter.UseResource((H5Writer hw) =>
					{
						tmpHw.CopyTo(hw, targetDataset, targetDataset);
					});
				}
				finally
				{
					if (tmpHw != null)
					{
						((IDisposable)tmpHw).Dispose();
					}
				}
			}
			catch
			{
				throw;
			}
			finally
			{
				FileHelpers.TryDelete(path);
			}
		});
		pr.ReportProgress(100);
	}

	public List<Message> ReadStructuredLogs(string datasetName)
	{
		ReadAttribute(datasetName, "MessageClasses_Code", out string[] values);
		ReadAttribute(datasetName, "MessageClasses_DefaultFormattedMessage", out string[] values2);
		ReadAttribute(datasetName, "Severity", out byte[] values3);
		ReadAttribute(datasetName, "MessageClassIndex", out int[] values4);
		ReadAttribute(datasetName, "EvaluatedMessage", out string[] values5);
		MessageClass[] messageClasss = new MessageClass[values.Length];
		MessageSeverity[] messageSeverities = new MessageSeverity[values.Length];
		for (int i = 0; i < values.Length; i++)
		{
			messageSeverities[i] = MessageSeverity.Information;
		}
		for (int j = 0; j < values4.Length; j++)
		{
			int values42 = values4[j];
			if (values42 >= 0 && values42 < values.Length)
			{
				messageSeverities[values42] = ByteToMessageSeverity(values3[j]);
			}
		}
		for (int k = 0; k < values.Length; k++)
		{
			string code = values[k];
			string message = values2[k];
			MessageSeverity severity = messageSeverities[k];
			messageClasss[k] = new MessageClass(code, severity, message);
		}
		List<Message> messages = new List<Message>();
		for (int l = 0; l < values3.Length; l++)
		{
			_ = values3[l];
			int values43 = values4[l];
			_ = values5[l];
			if (values43 < 0 || values43 >= messageClasss.Length)
			{
				throw new Exception($"Invalid MessageClassIndex {values43} at entry {l}");
			}
			MessageClass type = messageClasss[values43];
			Message message2 = new Message(type);
			messages.Add(message2);
		}
		return messages;
	}

	private MessageSeverity ByteToMessageSeverity(byte severity)
	{
		return severity switch
		{
			0 => MessageSeverity.Trace, 
			1 => MessageSeverity.Debug, 
			2 => MessageSeverity.Information, 
			3 => MessageSeverity.Warning, 
			4 => MessageSeverity.Error, 
			_ => MessageSeverity.Information, 
		};
	}

	public string DebugDisplay(bool includeAttrs = true)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine(Filename);
		sb.AppendLine("-----------------");
		DebugDisplayRecurse("/", "  ");
		return sb.ToString();
		void DebugDisplayRecurse(string grp, string prefix)
		{
			List<string> groupNames = GetGroupNames(grp);
			List<string> fullyQualifiedGroupNames = GetFullyQualifiedGroupNames(grp);
			List<string> datasetNames = GetDatasetNames(grp);
			List<string> fullyQualifiedDatasetNames = GetFullyQualifiedDatasetNames(grp);
			for (int i = 0; i < datasetNames.Count; i++)
			{
				string datasetName = fullyQualifiedDatasetNames[i];
				long[] datasetDimensions = GetDatasetDimensions(datasetName);
				Type datasetType = GetDatasetType(datasetName);
				string prefixDatasetNamesTerm = prefix + "-- " + datasetNames[i] + "  (" + datasetType.Name + string.Join(",", datasetDimensions).InSquareBrackets() + ")";
				if (DatasetIsVariableLength(datasetName))
				{
					prefixDatasetNamesTerm += " (VLEN)";
				}
				sb.AppendLine(prefixDatasetNamesTerm);
				if (includeAttrs)
				{
					DisplayAttrs(prefix, fullyQualifiedDatasetNames[i]);
				}
			}
			for (int j = 0; j < groupNames.Count; j++)
			{
				sb.AppendLine(prefix + "> " + groupNames[j]);
				if (includeAttrs)
				{
					DisplayAttrs(prefix, fullyQualifiedGroupNames[j]);
				}
				DebugDisplayRecurse(fullyQualifiedGroupNames[j], prefix + "  ");
			}
		}
		void DisplayAttrs(string prefix, string dsOrGroup)
		{
			foreach (KeyValuePair<string, object> allAttribute in ReadAllAttributes(dsOrGroup))
			{
				sb.AppendLine(prefix + "  :: " + allAttribute.Key + " -> " + allAttribute.Value.ToString());
			}
		}
	}

	public bool DebugCheckHandles()
	{
		lock (_handleLifecycleSync)
		{
			if (IsDisposed)
			{
				return false;
			}

			long fileId = Interlocked.Exchange(ref FileId, -1L);
			if (fileId < 0 || H5F.close(fileId) < 0)
			{
				return false;
			}
			bool result = FileHelpers.IsFileWritable(Filename);
			uint flags = 0u;
			long reopenedFileId = H5F.open(Filename, flags, 0L);
			Interlocked.Exchange(ref FileId, reopenedFileId);
			if (reopenedFileId < 0)
			{
				return false;
			}
			return result;
		}
	}
}
