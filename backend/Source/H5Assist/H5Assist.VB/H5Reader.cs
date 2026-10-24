using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using H5Assist.Caching;
using H5Assist.Chunking;
using H5Assist.Identifiers;
using HDF.PInvoke;
using Microsoft.Extensions.Logging;
using Utility.Compression;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;

namespace H5Assist.VB;

public class H5Reader : IDisposable
{
	[Flags]
	protected enum Log
	{
		None = 0,
		RecordOperation = 1,
		SafetyChecks = 2
	}

	protected long FileId;

	private static readonly ILogger _logger = LogHelper.GetLogger("H5Reader");

	protected string _filename;

	public const string Root = "/";

	public const string PathSeparator = "/";

	internal static readonly string CURRENT_LOCATION = ".";

	internal static readonly byte[] CURRENT_LOCATION_B = new byte[1] { 46 };

	public static int CHUNK_CACHE_SIZE_BYTES = 10485760;

	protected static Log _log = Log.SafetyChecks;

	private bool _wasClosed;

	private ConcurrentDictionary<H5DatasetID, long[]> _cachedChunkSizes = new ConcurrentDictionary<H5DatasetID, long[]>();

	public static string[] PathSeparatorChars = new string[2] { "/", "\\" };

	private readonly Dictionary<string, DatasetParameters> _parameters = new Dictionary<string, DatasetParameters>();

	protected static ThreadLocal<byte[]> _compressionBuffers = new ThreadLocal<byte[]>(() => new byte[256]);

	public long H5FileID => FileId;

	public string Filename => _filename;

	public static bool IsSafetyChecksOn => (_log & Log.SafetyChecks) > Log.None;

	public bool IsDisposed => _wasClosed;

	public static void SetLogging(bool log)
	{
		if (log)
		{
			_log |= Log.RecordOperation;
		}
		else
		{
			_log &= ~Log.RecordOperation;
		}
	}

	public static void SetSafetyChecks(bool boolValue)
	{
		if (boolValue)
		{
			_log |= Log.SafetyChecks;
		}
		else
		{
			_log &= ~Log.SafetyChecks;
		}
	}

	public H5Reader(string filename)
		: this(filename, 0, SWMR_Mode: false)
	{
	}

	public H5Reader(string fileName, int cacheSizeBytes, bool SWMR_Mode)
	{
		_filename = fileName;
		if (File.Exists(fileName))
		{
			if (cacheSizeBytes <= 0)
			{
				uint ordinal = 0u;
				if (SWMR_Mode)
				{
					ordinal |= 0x40;
				}
				FileId = H5F.open(fileName, ordinal, 0L);
			}
			else
			{
				long plistWithCacheSize = GetPlistWithCacheSize(cacheSizeBytes);
				FileId = H5F.open(fileName, 0u, plistWithCacheSize);
				H5P.close(plistWithCacheSize);
			}
			MarkStackTrace();
			return;
		}
		throw new Exception("Operation failed, does " + fileName + " exist?");
	}

	protected H5Reader()
	{
		MarkStackTrace();
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

	private void MarkStackTrace()
	{
	}

	protected long GetPlistWithCacheSize(int cacheSizeBytes)
	{
		long create = H5P.create(H5P.FILE_ACCESS);
		H5P.set_cache(create, 0, 521, cacheSizeBytes, 0.75);
		return create;
	}

	~H5Reader()
	{
	}

	public void Dispose()
	{
		_wasClosed = true;
		H5F.close(FileId);
	}

	[Conditional("DEBUG")]
	protected void LogOperation(string desc, [CallerMemberName] string function = "")
	{
	}

	public static bool IsValid(string filename)
	{
		try
		{
			using (new H5Reader(filename))
			{
			}
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public List<string> GetAttributeNames(string objectName)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
		List<string> attributeNames = new List<string>();
		long open = H5O.open(FileId, objectName, 0L);
		H5O.info_t oinfo = default;
		H5O.get_info(open, ref oinfo);
		int num_attrs = (int)oinfo.num_attrs;
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		byte[] byteValues = new byte[20];
		byte[] obj_name = new byte[1] { 46 };
		for (int i = 0; i <= num_attrs - 1; i++)
		{
			int get_name_by_idx = (int)H5A.get_name_by_idx(open, obj_name, H5.index_t.NAME, H5.iter_order_t.NATIVE, (ulong)i, byteValues, byteValues.Length, 0L);
			get_name_by_idx++;
			if (get_name_by_idx > byteValues.Length)
			{
				byteValues = new byte[get_name_by_idx];
				H5A.get_name_by_idx(open, obj_name, H5.index_t.NAME, H5.iter_order_t.NATIVE, (ulong)i, byteValues, byteValues.Length, 0L);
			}
			attributeNames.Add(aSCIIEncoding.GetString(byteValues, 0, get_name_by_idx).Trim(Convert.ToChar(0)));
		}
		H5O.close(open);
		return attributeNames;
	}

	public Type GetAttributeType(string objectName, string attributeName)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
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
		_ = _log & Log.RecordOperation;
		_ = 0;
		Dictionary<string, Type> attributeNamesAndTypes = new Dictionary<string, Type>();
		long open = H5O.open(FileId, objectName, 0L);
		H5O.info_t oinfo = default;
		H5O.get_info(open, ref oinfo);
		int num_attrs = (int)oinfo.num_attrs;
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		byte[] byteValues = new byte[20];
		byte[] obj_name = new byte[1] { 46 };
		for (int i = 0; i <= num_attrs - 1; i++)
		{
			int get_name_by_idx = (int)H5A.get_name_by_idx(open, obj_name, H5.index_t.NAME, H5.iter_order_t.NATIVE, (ulong)i, byteValues, byteValues.Length, 0L);
			get_name_by_idx++;
			if (get_name_by_idx > byteValues.Length)
			{
				byteValues = new byte[get_name_by_idx];
				H5A.get_name_by_idx(open, obj_name, H5.index_t.NAME, H5.iter_order_t.NATIVE, (ulong)i, byteValues, byteValues.Length, 0L);
			}
			string trim = aSCIIEncoding.GetString(byteValues, 0, get_name_by_idx).Trim(Convert.ToChar(0));
			Type attributeType = GetAttributeType(open, trim);
			attributeNamesAndTypes[trim] = attributeType;
		}
		H5O.close(open);
		return attributeNamesAndTypes;
	}

	public int GetAttributeNDims(string objectName, string AttributeName)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
		long open = H5O.open(FileId, objectName, 0L);
		long attr_id = H5A.open(open, AttributeName, 0L);
		long space_id = H5A.get_space(attr_id);
		int result = H5S.get_simple_extent_ndims(space_id);
		H5A.close(attr_id);
		H5S.close(space_id);
		H5O.close(open);
		return result;
	}

	public int GetNumAttributes(string datasetOrGroupName)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
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
		_ = _log & Log.RecordOperation;
		_ = 0;
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			EnsureDatasetExists(datasetName, "GetChunkSize");
		}
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
			H5P.get_chunk(createPropertyListId, get_simple_extent_ndims, ulongs);
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
		_ = _log & Log.RecordOperation;
		_ = 0;
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
		_ = _log & Log.RecordOperation;
		_ = 0;
		return GetLinkNames(groupPath, H5O.type_t.DATASET);
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
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			EnsureGroupExists(groupPath, "GetLinkNames");
		}
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
		_ = _log & Log.RecordOperation;
		_ = 0;
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			EnsureDatasetExists(datasetName, "GetDatasetNDims");
		}
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: false, needSpace: true);
		return H5S.get_simple_extent_ndims(h5DatasetID.SpaceId);
	}

	public long[] GetDatasetDimensions(string datasetName)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			EnsureDatasetExists(datasetName, "GetDatasetDimensions");
		}
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: false, needSpace: true);
		return GetDatasetDimensions(dataset);
	}

	public long[] GetDatasetDimensions(H5DatasetID dataset)
	{
		return UlongToLong(GetDimensions(dataset.SpaceId));
	}

	public Type GetDatasetType(string datasetName)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			EnsureDatasetExists(datasetName, "GetDatasetType");
		}
		using H5DatasetID datasetId = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: false);
		return H5DataTypeID.TypeConvert(datasetId, 0);
	}

	public bool DatasetIsVariableLength(string datasetName)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			EnsureDatasetExists(datasetName, "DatasetIsVariableLength");
		}
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: false);
		return H5T.is_variable_str(h5DatasetID.DataType.TypeId) > 0 || H5T.get_size(h5DatasetID.DataType.TypeId) == H5T.VARIABLE;
	}

	public bool PathIsExternalLink(string absolutePath)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
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
		string[] parts = Encoding.Default.GetString(byteValues).Split('\0');
		fileName = parts[1];
		dataSet = parts[2];
		gCHandle.Free();
		return true;
	}

	public bool ExistsAsGroup(string absolutePath)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
		return ObjectOfTypeExists(absolutePath, H5O.type_t.GROUP);
	}

	public bool ExistsAsDataset(string absolutePath)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
		return ObjectOfTypeExists(absolutePath, H5O.type_t.DATASET);
	}

	public bool ExistsAsAttribute(string absolutePath, string attributeName)
	{
		if ((_log & Log.RecordOperation) > Log.None)
		{
			LogAttributeOperation<object>(absolutePath, attributeName, "ExistsAsAttribute");
		}
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

	private void AttributePreCheck<T>(string objectName, string attributeName, [CallerMemberName] string callerName = "")
	{
		if ((_log & Log.RecordOperation) > Log.None)
		{
			LogAttributeOperation<T>(objectName, attributeName, callerName);
		}
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			EnsureAttributeExists(objectName, attributeName, callerName);
		}
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

	protected void LogAttributeOperation<T>(string objectName, string attributeName, [CallerMemberName] string callerName = "")
	{
	}

	private void H5AReadInternal<T>(long attributeId, long typeId, T[] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5A.read(attributeId, typeId, nint2);
		});
	}

	private void H5AReadInternal<T>(long attributeId, long typeId, T[,] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5A.read(attributeId, typeId, nint2);
		});
	}

	private void H5AReadInternal<T>(long attributeId, long typeId, T[,,] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5A.read(attributeId, typeId, nint2);
		});
	}

	public void ReadAttributeScalar<T>(string objectName, string attributeName, ref T trimGetStringTerm)
	{
		AttributePreCheck<T>(objectName, attributeName, "ReadAttributeScalar");
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		AttributeIntegrityCheck<T>(h5AttributeID, 0, "ReadAttributeScalar");
		long attributeId = h5AttributeID.AttributeId;
		long typeId = h5AttributeID.DataType.TypeId;
		_ = h5AttributeID.SpaceId;
		if ((object)typeof(T) == typeof(string))
		{
			ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
			byte[] byteValues = new byte[(int)H5T.get_size(typeId)];
			H5AReadInternal(attributeId, typeId, byteValues);
			trimGetStringTerm = (T)(object)aSCIIEncoding.GetString(byteValues).Trim(Convert.ToChar(0));
		}
		else
		{
			T[] values = new T[1];
			H5AReadInternal(attributeId, typeId, values);
			trimGetStringTerm = values[0];
		}
	}

	public void ReadAttribute<T>(string objectName, string attributeName, ref T[] values)
	{
		AttributePreCheck<T>(objectName, attributeName, "ReadAttribute");
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		AttributeIntegrityCheck<T>(h5AttributeID, 1, "ReadAttribute");
		h5AttributeID.GetNativeIDs(out var attrID, out var datatypeID, out var dataspaceID);
		ulong[] ensureFullSize = EnsureFullSize(dataspaceID, ref values);
		if ((object)values.GetType() == typeof(string[]))
		{
			int get_size = (int)H5T.get_size(datatypeID);
			byte[] byteValues = new byte[(int)ensureFullSize[0] * get_size];
			H5AReadInternal(attrID, datatypeID, byteValues);
			ParseString(ensureFullSize, byteValues, get_size, values);
		}
		else
		{
			H5AReadInternal(attrID, datatypeID, values);
		}
	}

	public void ReadAttribute<T>(string objectName, string attributeName, ref T[,] values)
	{
		AttributePreCheck<T>(objectName, attributeName, "ReadAttribute");
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		AttributeIntegrityCheck<T>(h5AttributeID, 2, "ReadAttribute");
		h5AttributeID.GetNativeIDs(out var attrID, out var datatypeID, out var dataspaceID);
		ulong[] ensureFullSize = EnsureFullSize(dataspaceID, ref values);
		if ((object)values.GetType() == typeof(string[,]))
		{
			int get_size = (int)H5T.get_size(datatypeID);
			byte[,] byteValues = new byte[(uint)ensureFullSize[0], (int)ensureFullSize[1] * get_size];
			H5AReadInternal(attrID, datatypeID, byteValues);
			ParseString(ensureFullSize, byteValues, get_size, values);
		}
		else
		{
			H5AReadInternal(attrID, datatypeID, values);
		}
	}

	public void ReadAttribute<T>(string objectName, string attributeName, ref T[,,] values)
	{
		if (typeof(string) == typeof(T))
		{
			throw new NotImplementedException("3D string attributes are not supported.");
		}
		AttributePreCheck<T>(objectName, attributeName, "ReadAttribute");
		using H5AttributeID h5AttributeID = H5AttributeID.Open(FileId, objectName, attributeName, needType: true, needSpace: true);
		AttributeIntegrityCheck<T>(h5AttributeID, 3, "ReadAttribute");
		h5AttributeID.GetNativeIDs(out var attrID, out var datatypeID, out var dataspaceID);
		EnsureFullSize(dataspaceID, ref values);
		H5AReadInternal(attrID, datatypeID, values);
	}

	public bool TryReadAttributeScalar<T>(string objectName, string attributeName, ref T localValue)
	{
		try
		{
			if (!ExistsAsAttribute(objectName, attributeName))
			{
				return false;
			}
			ReadAttributeScalar(objectName, attributeName, ref localValue);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public bool TryReadAttribute<T>(string objectName, string attributeName, ref T[] values)
	{
		try
		{
			if (!ExistsAsAttribute(objectName, attributeName))
			{
				return false;
			}
			ReadAttribute(objectName, attributeName, ref values);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public bool TryReadAttribute<T>(string objectName, string attributeName, ref T[,] values)
	{
		try
		{
			if (!ExistsAsAttribute(objectName, attributeName))
			{
				return false;
			}
			ReadAttribute(objectName, attributeName, ref values);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public bool TryReadAttribute<T>(string objectName, string attributeName, ref T[,,] values)
	{
		try
		{
			if (!ExistsAsAttribute(objectName, attributeName))
			{
				return false;
			}
			ReadAttribute(objectName, attributeName, ref values);
			return true;
		}
		catch
		{
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
			string textValue = string.Empty;
			ReadAttributeScalar(objectName, attributeName, ref textValue);
			return DiskNameAttribute.TryDeserialize<T>(textValue, out localValue);
		}
		catch
		{
			localValue = default;
			return false;
		}
	}

	public float TryReadSingleAttributeScalar(string setOrGroupName, string attributeName, float defaultValue = float.NaN)
	{
		float computed = 0f;
		if (TryReadAttributeScalar(setOrGroupName, attributeName, ref computed))
		{
			return computed;
		}
		return defaultValue;
	}

	public string TryReadStringAttributeScalar(string setOrGroupName, string attributeName, string defaultValue = "")
	{
		string textValue = "";
		if (TryReadAttributeScalar(setOrGroupName, attributeName, ref textValue))
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
					byte localValue = 0;
					ReadAttributeScalar(dsetOrGroupname, key, ref localValue);
					allAttributes.Add(key, localValue);
				}
				else if (type == typeof(short))
				{
					short ordinal = 0;
					ReadAttributeScalar(dsetOrGroupname, key, ref ordinal);
					allAttributes.Add(key, ordinal);
				}
				else if (type == typeof(int))
				{
					int ordinal2 = 0;
					ReadAttributeScalar(dsetOrGroupname, key, ref ordinal2);
					allAttributes.Add(key, ordinal2);
				}
				else if (type == typeof(long))
				{
					long ordinal3 = 0L;
					ReadAttributeScalar(dsetOrGroupname, key, ref ordinal3);
					allAttributes.Add(key, ordinal3);
				}
				else if (type == typeof(float))
				{
					float computed = 0f;
					ReadAttributeScalar(dsetOrGroupname, key, ref computed);
					allAttributes.Add(key, computed);
				}
				else if (type == typeof(double))
				{
					double computed2 = 0.0;
					ReadAttributeScalar(dsetOrGroupname, key, ref computed2);
					allAttributes.Add(key, computed2);
				}
				else if (type == typeof(string))
				{
					string textValue = "";
					ReadAttributeScalar(dsetOrGroupname, key, ref textValue);
					allAttributes.Add(key, textValue);
				}
				else if (type == typeof(bool))
				{
					bool condition = false;
					ReadAttributeScalar(dsetOrGroupname, key, ref condition);
					allAttributes.Add(key, condition);
				}
				break;
			case 1:
				if (type == typeof(byte))
				{
					byte[] values17 = null;
					ReadAttribute(dsetOrGroupname, key, ref values17);
					allAttributes.Add(key, values17);
				}
				else if (type == typeof(short))
				{
					short[] values18 = null;
					ReadAttribute(dsetOrGroupname, key, ref values18);
					allAttributes.Add(key, values18);
				}
				else if (type == typeof(int))
				{
					int[] values19 = null;
					ReadAttribute(dsetOrGroupname, key, ref values19);
					allAttributes.Add(key, values19);
				}
				else if (type == typeof(long))
				{
					long[] values20 = null;
					ReadAttribute(dsetOrGroupname, key, ref values20);
					allAttributes.Add(key, values20);
				}
				else if (type == typeof(float))
				{
					float[] values21 = null;
					ReadAttribute(dsetOrGroupname, key, ref values21);
					allAttributes.Add(key, values21);
				}
				else if (type == typeof(double))
				{
					double[] values22 = null;
					ReadAttribute(dsetOrGroupname, key, ref values22);
					allAttributes.Add(key, values22);
				}
				else if (type == typeof(string))
				{
					string[] values23 = null;
					ReadAttribute(dsetOrGroupname, key, ref values23);
					allAttributes.Add(key, values23);
				}
				else if (type == typeof(bool))
				{
					bool[] values24 = null;
					ReadAttribute(dsetOrGroupname, key, ref values24);
					allAttributes.Add(key, values24);
				}
				break;
			case 2:
				if (type == typeof(byte))
				{
					byte[,] values9 = null;
					ReadAttribute(dsetOrGroupname, key, ref values9);
					allAttributes.Add(key, values9);
				}
				else if (type == typeof(short))
				{
					short[,] values10 = null;
					ReadAttribute(dsetOrGroupname, key, ref values10);
					allAttributes.Add(key, values10);
				}
				else if (type == typeof(int))
				{
					int[,] values11 = null;
					ReadAttribute(dsetOrGroupname, key, ref values11);
					allAttributes.Add(key, values11);
				}
				else if (type == typeof(long))
				{
					long[,] values12 = null;
					ReadAttribute(dsetOrGroupname, key, ref values12);
					allAttributes.Add(key, values12);
				}
				else if (type == typeof(float))
				{
					float[,] values13 = null;
					ReadAttribute(dsetOrGroupname, key, ref values13);
					allAttributes.Add(key, values13);
				}
				else if (type == typeof(double))
				{
					double[,] values14 = null;
					ReadAttribute(dsetOrGroupname, key, ref values14);
					allAttributes.Add(key, values14);
				}
				else if (type == typeof(string))
				{
					string[,] values15 = null;
					ReadAttribute(dsetOrGroupname, key, ref values15);
					allAttributes.Add(key, values15);
				}
				else if (type == typeof(bool))
				{
					bool[,] values16 = null;
					ReadAttribute(dsetOrGroupname, key, ref values16);
					allAttributes.Add(key, values16);
				}
				break;
			case 3:
				if (type == typeof(byte))
				{
					byte[,,] values = null;
					ReadAttribute(dsetOrGroupname, key, ref values);
					allAttributes.Add(key, values);
				}
				else if (type == typeof(short))
				{
					short[,,] values2 = null;
					ReadAttribute(dsetOrGroupname, key, ref values2);
					allAttributes.Add(key, values2);
				}
				else if (type == typeof(int))
				{
					int[,,] values3 = null;
					ReadAttribute(dsetOrGroupname, key, ref values3);
					allAttributes.Add(key, values3);
				}
				else if (type == typeof(long))
				{
					long[,,] values4 = null;
					ReadAttribute(dsetOrGroupname, key, ref values4);
					allAttributes.Add(key, values4);
				}
				else if (type == typeof(float))
				{
					float[,,] values5 = null;
					ReadAttribute(dsetOrGroupname, key, ref values5);
					allAttributes.Add(key, values5);
				}
				else if (type == typeof(double))
				{
					double[,,] values6 = null;
					ReadAttribute(dsetOrGroupname, key, ref values6);
					allAttributes.Add(key, values6);
				}
				else if (type == typeof(string))
				{
					string[,,] values7 = null;
					ReadAttribute(dsetOrGroupname, key, ref values7);
					allAttributes.Add(key, values7);
				}
				else if (type == typeof(bool))
				{
					bool[,,] values8 = null;
					ReadAttribute(dsetOrGroupname, key, ref values8);
					allAttributes.Add(key, values8);
				}
				break;
			}
		}
		return allAttributes;
	}

	public DatasetParameters GetDatasetParameters(string dset)
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

	protected void DatasetPreCheck<T>(string datasetName, [CallerMemberName] string callerName = "")
	{
		if ((_log & Log.RecordOperation) > Log.None)
		{
			LogDatasetOperation<T>(datasetName, callerName);
		}
		if ((_log & Log.SafetyChecks) > Log.None && !ExistsAsDataset(datasetName))
		{
			throw new ObjectNotFoundException(datasetName, callerName);
		}
	}

	private void DatasetIntegrityCheck<T>(H5DatasetID dset, int nDims, [CallerMemberName] string callerName = "")
	{
		if ((_log & Log.SafetyChecks) != Log.None)
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
	}

	private void LogDatasetOperation<T>(string objectName, [CallerMemberName] string callerName = "")
	{
	}

	protected void H5DReadInternal<T>(long datasetId, long typeId, T[] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.read(datasetId, typeId, 0L, 0L, 0L, nint2);
		});
	}

	protected unsafe void H5DReadInternal<T>(long datasetId, long typeId, Memory<T> buffer) where T : unmanaged
	{
		fixed (T* buf = buffer.Span)
		{
			H5D.read(datasetId, typeId, 0L, 0L, 0L, (nint)buf);
		}
	}

	protected void H5DReadInternal<T>(long datasetId, long typeId, T[,] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.read(datasetId, typeId, 0L, 0L, 0L, nint2);
		});
	}

	protected void H5DReadInternal<T>(long datasetId, long typeId, T[,,] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.read(datasetId, typeId, 0L, 0L, 0L, nint2);
		});
	}

	protected void H5DReadInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, T[] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.read(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, nint2);
		});
	}

	protected void H5DReadInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, T[,] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.read(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, nint2);
		});
	}

	protected void H5DReadInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, T[,,] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.read(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, nint2);
		});
	}

	protected unsafe void H5DReadInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, Memory<T> buffer) where T : unmanaged
	{
		fixed (T* buf = buffer.Span)
		{
			H5D.read(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, (nint)buf);
		}
	}

	public unsafe void ReadDataset<T>(string datasetName, ref T[] values)
	{
		DatasetPreCheck<T>(datasetName, "ReadDataset");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<T>(h5DatasetID, 1, "ReadDataset");
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] ensureFullSize = EnsureFullSize(dataspaceID, ref values);
		if (values.Length == 0)
		{
			return;
		}
		if (typeof(T) == typeof(string))
		{
			string[] strings = (string[])(object)values;
			if (H5T.is_variable_str(datatypeID) > 0)
			{
				long h5TC_S1Term = H5T.copy(H5T.C_S1);
				H5T.set_size(h5TC_S1Term, H5T.VARIABLE);
				nint[] nints = new nint[values.Length];
				fixed (nint* buf = nints)
				{
					H5DReadInternal(datasetID, datatypeID, nints);
					ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
					for (int i = 0; i < values.Length; i++)
					{
						byte* nint2 = (byte*)nints[i];
						int byteCount = 0;
						for (int j = 0; j < int.MaxValue; j++)
						{
							if (nint2[j] == 0)
							{
								byteCount = j;
								break;
							}
						}
						strings[i] = aSCIIEncoding.GetString(nint2, byteCount);
					}
					H5D.vlen_reclaim(h5TC_S1Term, dataspaceID, 0L, (nint)buf);
					return;
				}
			}
			int get_size = (int)H5T.get_size(datatypeID);
			byte[] byteValues = new byte[(int)ensureFullSize[0] * get_size];
			H5DReadInternal(datasetID, datatypeID, byteValues);
			ParseString(ensureFullSize, byteValues, get_size, values);
		}
		else
		{
			H5DReadInternal(datasetID, datatypeID, values);
		}
	}

	public void ReadDatasetAs<T>(string datasetName, ref T[] values) where T : IConvertible
	{
		DatasetPreCheck<T>(datasetName, "ReadDatasetAs");
		Type datasetType = GetDatasetType(datasetName);
		if (datasetType == typeof(T))
		{
			ReadDataset(datasetName, ref values);
			return;
		}
		Array values6 = null;
		if (datasetType == typeof(byte))
		{
			byte[] values2 = null;
			ReadDataset(datasetName, ref values2);
			values6 = values2;
		}
		else if (datasetType == typeof(short))
		{
			short[] values3 = null;
			ReadDataset(datasetName, ref values3);
			values6 = values3;
		}
		else if (datasetType == typeof(int))
		{
			int[] values4 = null;
			ReadDataset(datasetName, ref values4);
			values6 = values4;
		}
		else
		{
			if (!(datasetType == typeof(long)))
			{
				throw new NotImplementedException();
			}
			long[] values5 = null;
			ReadDataset(datasetName, ref values5);
			values6 = values5;
		}
		if (values == null || values.Length != values6.Length)
		{
			values = new T[values6.Length];
		}
		for (int i = 0; i < values6.Length; i++)
		{
			values[i] = (T)Convert.ChangeType(values6.GetValue(i), typeof(T));
		}
	}

	public void ReadDataset<T>(string datasetName, ref T[,] values)
	{
		DatasetPreCheck<T>(datasetName, "ReadDataset");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<T>(h5DatasetID, 2, "ReadDataset");
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] ensureFullSize = EnsureFullSize(dataspaceID, ref values);
		if (values.Length != 0)
		{
			if ((object)values.GetType() == typeof(string[,]))
			{
				int get_size = (int)H5T.get_size(datatypeID);
				byte[,] byteValues = new byte[Convert.ToInt32(ensureFullSize[0]), Convert.ToInt32(ensureFullSize[1]) * get_size];
				H5DReadInternal(datasetID, datatypeID, byteValues);
				ParseString(ensureFullSize, byteValues, get_size, values);
			}
			else
			{
				H5DReadInternal(datasetID, datatypeID, values);
			}
		}
	}

	public void ReadDataset<T>(string datasetName, ref T[,,] values)
	{
		DatasetPreCheck<T>(datasetName, "ReadDataset");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<T>(h5DatasetID, 3, "ReadDataset");
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] ensureFullSize = EnsureFullSize(dataspaceID, ref values);
		if (values.Length != 0)
		{
			if ((object)values.GetType() == typeof(string[,,]))
			{
				int get_size = (int)H5T.get_size(datatypeID);
				byte[,,] byteValues = new byte[Convert.ToInt32(ensureFullSize[0]), Convert.ToInt32(ensureFullSize[1]), Convert.ToInt32(ensureFullSize[2]) * get_size];
				H5DReadInternal(datasetID, datatypeID, byteValues);
				ParseString(ensureFullSize, byteValues, get_size, values);
			}
			else
			{
				H5DReadInternal(datasetID, datatypeID, values);
			}
		}
	}

	protected void GetHyperslabRowDims(int startRow, int numRows, ulong[] size, out ulong[] hyperslabDims, out ulong[] offset)
	{
		GetHyperslab2DDims(startRow, numRows, 0, (int)size[1], out hyperslabDims, out offset);
	}

	private void GetHyperslabColDims(int startCol, int numCols, ulong[] size, out ulong[] hyperslabDims, out ulong[] offset)
	{
		GetHyperslab2DDims(0, (int)size[0], startCol, numCols, out hyperslabDims, out offset);
	}

	private void GetHyperslab2DDims(int startRow, int numRows, int startCol, int numCols, out ulong[] hyperslabDims, out ulong[] offset)
	{
		hyperslabDims = new ulong[2];
		hyperslabDims[0] = (ulong)numRows;
		hyperslabDims[1] = (ulong)numCols;
		offset = new ulong[2];
		offset[0] = (ulong)startRow;
		offset[1] = (ulong)startCol;
	}

	private void GetHyperslab1DDims(int startElement, int numElements, out ulong[] hyperslabDims, out ulong[] offset)
	{
		hyperslabDims = new ulong[1];
		hyperslabDims[0] = (ulong)numElements;
		offset = new ulong[1];
		offset[0] = (ulong)startElement;
	}

	private void RowColArgCheck(int startRow, int numRows, int startCol, int numCols, ulong[] size, string datasetName, [CallerMemberName] string callerName = "")
	{
		if (startRow < 0)
		{
			throw new ArgumentException("ERROR: Must have a non-negative starting row for reading dataset: " + datasetName + " in " + callerName);
		}
		if (numRows <= 0)
		{
			throw new ArgumentException("ERROR: Must have a positive row count for reading dataset: " + datasetName + " in " + callerName);
		}
		if (startCol < 0)
		{
			throw new ArgumentException("ERROR: Must have a non-negative starting column for reading dataset: " + datasetName + " in " + callerName);
		}
		if (numCols <= 0)
		{
			throw new ArgumentException("ERROR: Must have a positive column count for reading dataset: " + datasetName + " in " + callerName);
		}
		if (startRow >= (int)size[0])
		{
			throw new ArgumentException("Start row is beyond extent of dataset: " + datasetName + " in " + callerName);
		}
		if (numRows > (int)size[0] - startRow)
		{
			throw new ArgumentException("Start row + number of rows goes beyond extent of dataset: " + datasetName + " in " + callerName);
		}
		if (startCol >= (int)size[1])
		{
			throw new ArgumentException("Start column is beyond extent of dataset: " + datasetName + " in " + callerName);
		}
		if (numCols > (int)size[1] - startCol)
		{
			throw new ArgumentException("Start column + number of columns goes beyond extent of dataset: " + datasetName + " in " + callerName);
		}
	}

	private void ElementArgCheck(int startElement, int numElements, ulong[] size, string datasetName, [CallerMemberName] string callerName = "")
	{
		if (startElement < 0)
		{
			throw new ArgumentException("ERROR: Must have a non-negative starting element for reading dataset: " + datasetName + " in " + callerName);
		}
		if (numElements <= 0)
		{
			throw new ArgumentException("ERROR: Must have a positive element count for reading dataset: " + datasetName + " in " + callerName);
		}
		if (startElement >= (int)size[0])
		{
			throw new ArgumentException("Start element is beyond extent of dataset: " + datasetName + " in " + callerName);
		}
		if (numElements > (int)size[0] - startElement)
		{
			throw new ArgumentException("Start element + number of elements goes beyond extent of dataset: " + datasetName + " in " + callerName);
		}
	}

	public void ReadRow<T>(string dsetName, int row, ref T[,] values)
	{
		ReadRows(dsetName, row, 1, ref values);
	}

	public void ReadRow<T>(H5DatasetID d, int row, ref T[,] values)
	{
		ReadRows(d, row, 1, ref values);
	}

	public void ReadRows<T>(string datasetName, int startRow, int numRows, ref T[,] values)
	{
		if (typeof(T) == typeof(string))
		{
			string[,] values2 = (string[,])(object)values;
			ReadStringRows(datasetName, startRow, numRows, ref values2);
			values = (T[,])(object)values2;
			return;
		}
		DatasetPreCheck<T>(datasetName, "ReadRows");
		using (H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true))
		{
			DatasetIntegrityCheck<T>(h5DatasetID, 2, "ReadRows");
			h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
			ulong[] dimensions = GetDimensions(dataspaceID);
			if ((_log & Log.SafetyChecks) > Log.None)
			{
				RowColArgCheck(startRow, numRows, 0, (int)dimensions[1], dimensions, datasetName, "ReadRows");
			}
			GetHyperslabRowDims(startRow, numRows, dimensions, out var hyperslabDims, out var offset);
			RedimIfNecessary(ref values, hyperslabDims);
			using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, hyperslabDims, offset);
			H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, values);
		}
		_logger.LogTrace("{Dataset} read from {StartRow} ({RowCount} rows).", datasetName, startRow, numRows);
	}

	public T[,] ReadRows<T>(string datasetName, int startRow, int numRows)
	{
		T[,] values = null;
		ReadRows(datasetName, startRow, numRows, ref values);
		return values;
	}

	public T[,] ReadRows<T>(H5DatasetID d, int startRow, int numRows)
	{
		T[,] values = null;
		ReadRows(d, startRow, numRows, ref values);
		return values;
	}

	public H5DatasetID OpenDataset(string datasetName, int cacheSizeInBytes = 0)
	{
		DatasetPreCheck<object>(datasetName, "OpenDataset");
		return H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true, needCreatePropertyList: true, cacheSizeInBytes);
	}

	public void ReadRows<T>(H5DatasetID dataset, int startRow, int numRows, ref T[,] values)
	{
		if (typeof(T) == typeof(string))
		{
			throw new NotImplementedException();
		}
		DatasetIntegrityCheck<T>(dataset, 2, "ReadRows");
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataspaceID);
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			RowColArgCheck(startRow, numRows, 0, (int)dimensions[1], dimensions, dataset.DatasetName, "ReadRows");
		}
		GetHyperslabRowDims(startRow, numRows, dimensions, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, hyperslabDims);
		using (H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, hyperslabDims, offset))
		{
			H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, values);
		}
		_logger.LogTrace("{Dataset} read from {StartRow} ({RowCount} rows).", dataset.DatasetName, startRow, numRows);
	}

	public void ReadRows<T>(H5DatasetID dataset, int startRow, int numRows, Memory<T> buffer) where T : unmanaged
	{
		if (typeof(T) == typeof(string))
		{
			throw new NotImplementedException();
		}
		DatasetIntegrityCheck<T>(dataset, 2, "ReadRows");
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataspaceID);
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			RowColArgCheck(startRow, numRows, 0, (int)dimensions[1], dimensions, dataset.DatasetName, "ReadRows");
		}
		GetHyperslabRowDims(startRow, numRows, dimensions, out var hyperslabDims, out var offset);
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

	public void ReadRowVB<T>(string datasetName, int rowIdx, ref T[] values) where T : struct
	{
		Type typeFromHandle = typeof(T);
		if (typeFromHandle == typeof(float))
		{
			float[] values2 = (float[])(object)values;
			ReadRow(datasetName, rowIdx, ref values2);
			values = (T[])(object)values2;
			return;
		}
		if (typeFromHandle == typeof(byte))
		{
			byte[] values3 = (byte[])(object)values;
			ReadRow(datasetName, rowIdx, ref values3);
			values = (T[])(object)values3;
			return;
		}
		throw new NotImplementedException();
	}

	public void ReadRow<T>(string datasetName, int rowIdx, ref T[] values)
	{
		if (typeof(T) == typeof(string))
		{
			string[] values2 = (string[])(object)values;
			ReadStringRow(datasetName, rowIdx, ref values2);
			values = (T[])(object)values2;
			return;
		}
		DatasetPreCheck<T>(datasetName, "ReadRow");
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ReadRow(dataset, rowIdx, ref values);
	}

	public void ReadRow<T>(H5DatasetID dataset, int rowIdx, ref T[] values)
	{
		DatasetIntegrityCheck<T>(dataset, 2, "ReadRow");
		long datasetId = dataset.DatasetId;
		long typeId = dataset.DataType.TypeId;
		long spaceId = dataset.SpaceId;
		ulong[] dimensions = GetDimensions(spaceId);
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			RowColArgCheck(rowIdx, 1, 0, (int)dimensions[1], dimensions, dataset.DatasetName, "ReadRow");
		}
		GetHyperslabRowDims(rowIdx, 1, dimensions, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, new ulong[1] { hyperslabDims[1] });
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(spaceId, hyperslabDims, offset);
		H5DReadInternal(datasetId, typeId, spaceId, h5HyperslabID.SpaceId, values);
	}

	public void ReadRow<T>(H5DatasetID dataset, int rowIdx, Memory<T> buffer) where T : unmanaged
	{
		DatasetIntegrityCheck<T>(dataset, 2, "ReadRow");
		long datasetId = dataset.DatasetId;
		long typeId = dataset.DataType.TypeId;
		long spaceId = dataset.SpaceId;
		ulong[] dimensions = GetDimensions(spaceId);
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			RowColArgCheck(rowIdx, 1, 0, (int)dimensions[1], dimensions, dataset.DatasetName, "ReadRow");
		}
		GetHyperslabRowDims(rowIdx, 1, dimensions, out var hyperslabDims, out var offset);
		(new ulong[1])[0] = hyperslabDims[1];
		if (buffer.Length != (int)hyperslabDims[1])
		{
			throw new ArgumentException("Buffer incorrect size.");
		}
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(spaceId, hyperslabDims, offset);
		H5DReadInternal(datasetId, typeId, spaceId, h5HyperslabID.SpaceId, buffer);
	}

	public void ReadStringRows(string datasetName, int startRow, int numRows, ref string[,] values)
	{
		DatasetPreCheck<string>(datasetName, "ReadStringRows");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<string>(h5DatasetID, 2, "ReadStringRows");
		ulong[] dimensions = GetDimensions(h5DatasetID.SpaceId);
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			RowColArgCheck(startRow, numRows, 0, (int)dimensions[1], dimensions, datasetName, "ReadStringRows");
		}
		GetHyperslabRowDims(startRow, numRows, dimensions, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, hyperslabDims);
		byte[,] byteValues = null;
		int get_size = (int)H5T.get_size(h5DatasetID.DataType.TypeId);
		using (H5HyperslabID h5HyperslabID = H5HyperslabID.Open(h5DatasetID.SpaceId, hyperslabDims, offset))
		{
			byteValues = new byte[Convert.ToInt32(hyperslabDims[0]), Convert.ToInt32(hyperslabDims[1]) * get_size];
			H5DReadInternal(h5DatasetID.DatasetId, h5DatasetID.DataType.TypeId, h5DatasetID.SpaceId, h5HyperslabID.SpaceId, byteValues);
		}
		ParseString(hyperslabDims, byteValues, get_size, values);
	}

	public void ReadStringRow(string datasetName, int row, ref string[] values)
	{
		DatasetPreCheck<string>(datasetName, "ReadStringRow");
		using (H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true))
		{
			DatasetIntegrityCheck<string>(h5DatasetID, 2, "ReadStringRow");
			ulong[] dimensions = GetDimensions(h5DatasetID.SpaceId);
			if ((_log & Log.SafetyChecks) > Log.None)
			{
				RowColArgCheck(row, 1, 0, (int)dimensions[1], dimensions, datasetName, "ReadStringRow");
			}
			GetHyperslabRowDims(row, 1, dimensions, out var hyperslabDims, out var offset);
			RedimIfNecessary(ref values, hyperslabDims);
			byte[] byteValues = null;
			int get_size = (int)H5T.get_size(h5DatasetID.DataType.TypeId);
			using (H5HyperslabID h5HyperslabID = H5HyperslabID.Open(h5DatasetID.SpaceId, hyperslabDims, offset))
			{
				byteValues = new byte[(int)hyperslabDims[1] * get_size];
				H5DReadInternal(h5DatasetID.DatasetId, h5DatasetID.DataType.TypeId, h5DatasetID.SpaceId, h5HyperslabID.SpaceId, byteValues);
			}
			ParseString(hyperslabDims, byteValues, get_size, values);
		}
		throw new NotImplementedException();
	}

	public void ReadColumn<T>(string datasetName, int colIdx, ref T[] values)
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

	public void ReadColumn<T>(string datasetName, int colIdx, ref T[,] values)
	{
		ReadColumns(datasetName, colIdx, 1, ref values);
	}

	public void ReadColumn<T>(H5DatasetID dataset, int colIdx, ref T[] values)
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

	public void ReadColumn<T>(H5DatasetID dataset, int colIdx, ref T[,] values)
	{
		ReadColumns(dataset, colIdx, 1, ref values);
	}

	public void ReadColumns<T>(string datasetName, int startCol, int numCols, ref T[,] values)
	{
		DatasetPreCheck<T>(datasetName, "ReadColumns");
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ReadColumns(dataset, startCol, numCols, ref values);
	}

	public void ReadColumns<T>(H5DatasetID dataset, int startCol, int numCols, ref T[,] values)
	{
		DatasetIntegrityCheck<T>(dataset, 2, "ReadColumns");
		dataset.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataspaceID);
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			RowColArgCheck(0, (int)dimensions[0], startCol, numCols, dimensions, dataset.DatasetName, "ReadColumns");
		}
		GetHyperslabColDims(startCol, numCols, dimensions, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, hyperslabDims);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, hyperslabDims, offset);
		H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, values);
	}

	public void ReadColumns<T>(string dsetName, List<int> startColumns, ref T[,] values)
	{
		List<int> intList = new List<int>(startColumns.Count);
		for (int i = 1; i <= startColumns.Count; i++)
		{
			intList.Add(1);
		}
		ReadColumns(dsetName, startColumns, intList, ref values);
	}

	public void ReadColumns<T>(string datasetName, List<int> startColumns, List<int> numColumns, ref T[,] values)
	{
		if (startColumns == null || numColumns == null || startColumns.Count != numColumns.Count)
		{
			throw new ArgumentException("StartColumns/NumColumns are invalid.");
		}
		DatasetPreCheck<T>(datasetName, "ReadColumns");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<T>(h5DatasetID, 2, "ReadColumns");
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(dataspaceID);
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			for (int i = 0; i < startColumns.Count; i++)
			{
				int startCol = startColumns[i];
				int numCols = numColumns[i];
				RowColArgCheck(0, (int)dimensions[0], startCol, numCols, dimensions, datasetName, "ReadColumns");
			}
		}
		using H5HyperslabID h5HyperslabID = H5HyperslabID.CreateComplexColumns(startColumns, numColumns, dimensions, dataspaceID);
		RedimIfNecessary(ref values, h5HyperslabID.Dimensions);
		H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, values);
	}

	public void ReadElements<T>(string datasetName, int startElement, int numElements, ref T[] values)
	{
		if (typeof(T) == typeof(string))
		{
			string[] values2 = (string[])(object)values;
			ReadStringElements(datasetName, startElement, numElements, ref values2);
			values = (T[])(object)values2;
			return;
		}
		DatasetPreCheck<T>(datasetName, "ReadElements");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<T>(h5DatasetID, 1, "ReadElements");
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var dataspaceID);
		ulong[] dimensions = GetDimensions(h5DatasetID.SpaceId);
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			ElementArgCheck(startElement, numElements, dimensions, datasetName, "ReadElements");
		}
		GetHyperslab1DDims(startElement, numElements, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, hyperslabDims);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataspaceID, hyperslabDims, offset);
		H5DReadInternal(datasetID, datatypeID, dataspaceID, h5HyperslabID.SpaceId, values);
	}

	public void ReadStringElements(string datasetName, int startElement, int numElements, ref string[] values)
	{
		DatasetPreCheck<string>(datasetName, "ReadStringElements");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<string>(h5DatasetID, 1, "ReadStringElements");
		ulong[] dimensions = GetDimensions(h5DatasetID.SpaceId);
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			ElementArgCheck(startElement, numElements, dimensions, datasetName, "ReadStringElements");
		}
		GetHyperslab1DDims(startElement, numElements, out var hyperslabDims, out var offset);
		RedimIfNecessary(ref values, hyperslabDims);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(h5DatasetID.SpaceId, hyperslabDims, offset);
		int get_size = (int)H5T.get_size(h5DatasetID.DataType.TypeId);
		byte[] byteValues = new byte[Convert.ToInt32(hyperslabDims[0]) * get_size];
		H5DReadInternal(h5DatasetID.DatasetId, h5DatasetID.DataType.TypeId, h5DatasetID.SpaceId, h5HyperslabID.SpaceId, byteValues);
		ParseString(hyperslabDims, byteValues, get_size, values);
	}

	public Dictionary<CompoundFieldDefinition, Array> ReadCompoundElements(string datasetName)
	{
		DatasetPreCheck<CompoundFieldDefinition>(datasetName, "ReadCompoundElements");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		h5DatasetID.GetNativeIDs(out var datasetID, out var datatypeID, out var _);
		ulong[] dimensions = GetDimensions(h5DatasetID.SpaceId);
		byte[] byteValues = new byte[h5DatasetID.DataType.GetSize() * (int)dimensions[0]];
		H5DReadInternal(datasetID, datatypeID, byteValues);
		return DebyteifyObjects(byteValues, h5DatasetID.DataType);
	}

	public CompoundDataset ReadCompoundDataset(string datasetName)
	{
		return new CompoundDataset(ReadCompoundElements(datasetName));
	}

	public void MultiReadElements<T>(string datasetName, IList<int> startElements, IList<int> numElements, ref T[] values)
	{
		if (startElements == null || numElements == null || startElements.Count != numElements.Count)
		{
			throw new ArgumentException("StartElements/NumElements are invalid.");
		}
		DatasetPreCheck<string>(datasetName, "MultiReadElements");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		DatasetIntegrityCheck<string>(h5DatasetID, 1, "MultiReadElements");
		ulong[] dimensions = GetDimensions(h5DatasetID.SpaceId);
		if ((_log & Log.SafetyChecks) > Log.None)
		{
			for (int i = 0; i < startElements.Count; i++)
			{
				int startElement = startElements[i];
				int numElements2 = numElements[i];
				ElementArgCheck(startElement, numElements2, dimensions, datasetName, "MultiReadElements");
			}
		}
		using H5HyperslabID h5HyperslabID = H5HyperslabID.CreateComplexElements(numElements, startElements, dimensions, h5DatasetID.SpaceId);
		RedimIfNecessary(ref values, h5HyperslabID.Dimensions);
		H5DReadInternal(h5DatasetID.DatasetId, h5DatasetID.DataType.TypeId, h5DatasetID.SpaceId, h5HyperslabID.SpaceId, values);
	}

	public void ReadJaggedLine<T>(string infoDataset, string valuesDataset, int element, ref T[] values)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
		int[,] values2 = null;
		ReadRow(infoDataset, element, ref values2);
		int startElement = values2[0, 0];
		int numElements = values2[0, 1];
		ReadElements(valuesDataset, startElement, numElements, ref values);
	}

	public void ReadJaggedLine<T>(string infoDataset, string valuesDataset, int element, ref T[,] values)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
		int[,] values2 = null;
		ReadRow(infoDataset, element, ref values2);
		int startRow = values2[0, 0];
		int numRows = values2[0, 1];
		ReadRows(valuesDataset, startRow, numRows, ref values);
	}

	public void ReadJagged<T>(string infoDataset, string valueDataset, Action<T[,]> nextElement)
	{
		if (!ExistsAsDataset(infoDataset))
		{
			throw new ArgumentException(infoDataset + " does not exist.");
		}
		if (!ExistsAsDataset(valueDataset))
		{
			throw new ArgumentException(infoDataset + " does not exist.");
		}
		int[,] values = null;
		ReadDataset(infoDataset, ref values);
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

	public void ReadChunkDirectVB<T>(H5DatasetID dataset, int[] chunkIndex, ref T[] values) where T : struct
	{
		if (typeof(T) == typeof(float))
		{
			float[] values2 = (float[])(object)values;
			ReadChunkDirect(dataset, chunkIndex, ref values2);
			values = (T[])(object)values2;
			return;
		}
		throw new NotImplementedException();
	}

	public void ReadChunkDirectVB<T>(H5DatasetID dataset, int[] chunkIndex, ref T[,] values) where T : struct
	{
		if (typeof(T) == typeof(float))
		{
			float[,] values2 = (float[,])(object)values;
			ReadChunkDirect(dataset, chunkIndex, ref values2);
			values = (T[,])(object)values2;
			return;
		}
		throw new NotImplementedException();
	}

	public void ReadChunks<T>(H5DatasetID dataset, int[] chunkStartIndex, int[] chunkCount, out T[][] values)
	{
		if (chunkStartIndex == null || chunkCount == null || chunkStartIndex.Length != chunkCount.Length)
		{
			throw new ArgumentException("Chunk start index and chunk count need to be the same length.");
		}
		_ = new int[chunkStartIndex.Length];
		for (int i = 0; i < chunkStartIndex.Length; i++)
		{
			int chunkStartIndex2 = chunkStartIndex[i];
			int chunkCount2 = chunkCount[i];
			for (int j = chunkStartIndex2; j < chunkStartIndex2 + chunkCount2; j++)
			{
			}
		}
		throw new NotImplementedException();
	}

	private unsafe void ReadChunkDirectInternal<T>(H5DatasetID dataset, int[] chunkIndex, Action<Memory<byte>, long[]> useChunk)
	{
		uint filter_mask = 1u;
		long[] cachedChunkSize = GetCachedChunkSize(dataset);
		ulong[] ulongs = new ulong[chunkIndex.Length];
		for (int i = 0; i < chunkIndex.Length; i++)
		{
			ulongs[i] = (ulong)(chunkIndex[i] * cachedChunkSize[i]);
		}
		ulong chunk_bytes = 0uL;
		fixed (ulong* offset = ulongs)
		{
			H5D.get_chunk_storage_size(dataset.DatasetId, offset, ref chunk_bytes);
		}
		using IMemoryOwner<byte> memoryOwner = SharedMemoryPool<byte>.Rent((int)chunk_bytes);
		Memory<byte> slice = memoryOwner.Memory.Slice(0, (int)chunk_bytes);
		fixed (byte* buf = slice.Span)
		{
			Advanced.read_chunk(dataset.DatasetId, 0L, ulongs, ref filter_mask, (nint)buf);
		}
		useChunk(slice, cachedChunkSize);
	}

	public long CompressedSize(string dsetName)
	{
		long chunk_bytesSum = 0L;
		long[] chunkSize = GetChunkSize(dsetName);
		long[] datasetDimensions = GetDatasetDimensions(dsetName);
		long[] longs = new long[chunkSize.Length];
		for (int i = 0; i < chunkSize.Length; i++)
		{
			longs[i] = (long)Math.Ceiling((double)datasetDimensions[i] / (double)chunkSize[i]);
		}
		long[] longsArray = new long[chunkSize.Length];
		ulong[] ulongs = new ulong[chunkSize.Length];
		using H5DatasetID h5DatasetID = OpenDataset(dsetName);
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
					}
					lengthOffset--;
					continue;
				}
				longsArray[lengthOffset]++;
				for (int j = lengthOffset + 1; j < longsArray.Length; j++)
				{
					longsArray[lengthOffset] = 0L;
				}
				break;
			}
			if (condition)
			{
				break;
			}
			for (int k = 0; k < longsArray.Length; k++)
			{
				ulongs[k] = (ulong)(longsArray[k] * chunkSize[k]);
			}
			ulong chunk_bytes = 0uL;
			H5D.get_chunk_storage_size(h5DatasetID.DatasetId, ulongs, ref chunk_bytes);
			chunk_bytesSum += (long)chunk_bytes;
		}
		return chunk_bytesSum;
	}

	private static Dictionary<CompoundFieldDefinition, Array> DebyteifyObjects(byte[] bytes, H5DataTypeID datatype)
	{
		Dictionary<CompoundFieldDefinition, Array> valuesByCompoundFieldDefinition = new Dictionary<CompoundFieldDefinition, Array>();
		CompoundTypeDefinition compoundDataType = datatype.GetCompoundDataType();
		int sumSize = compoundDataType.SumSize();
		if (bytes.Length % sumSize != 0)
		{
			throw new ArgumentException("Incorrect compound buffer size");
		}
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		int count = (int)Math.Round((double)bytes.Length / (double)sumSize);
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
			int size = field.Size;
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
					for (intervalIndex = 0; intervalIndex < size && bytes[offset + intervalIndex] != 0; intervalIndex++)
					{
					}
					((string[])values)[i] = aSCIIEncoding.GetString(bytes, offset, intervalIndex);
				}
				offset += sumSize;
			}
			valuesByCompoundFieldDefinition[field] = values;
		}
		return valuesByCompoundFieldDefinition;
	}

	protected unsafe void PinPointer<T>(T[] buffer, Action<nint> Execute)
	{
		if (typeof(T) == typeof(byte))
		{
			fixed (byte* localValue = (byte[])(object)buffer)
			{
				void* localValue2 = localValue;
				Execute((nint)localValue2);
			}
			return;
		}
		if (typeof(T) == typeof(bool))
		{
			fixed (bool* localValue3 = (bool[])(object)buffer)
			{
				void* localValue4 = localValue3;
				Execute((nint)localValue4);
			}
			return;
		}
		if (typeof(T) == typeof(float))
		{
			fixed (float* localValue5 = (float[])(object)buffer)
			{
				void* localValue6 = localValue5;
				Execute((nint)localValue6);
			}
			return;
		}
		if (typeof(T) == typeof(double))
		{
			fixed (double* localValue7 = (double[])(object)buffer)
			{
				void* localValue8 = localValue7;
				Execute((nint)localValue8);
			}
			return;
		}
		if (typeof(T) == typeof(int))
		{
			fixed (int* localValue9 = (int[])(object)buffer)
			{
				void* localValue10 = localValue9;
				Execute((nint)localValue10);
			}
			return;
		}
		if (typeof(T) == typeof(long))
		{
			fixed (long* localValue11 = (long[])(object)buffer)
			{
				void* localValue12 = localValue11;
				Execute((nint)localValue12);
			}
			return;
		}
		if (typeof(T) == typeof(nint))
		{
			fixed (nint* nintTerm = (nint[])(object)buffer)
			{
				Execute((nint)nintTerm);
			}
			return;
		}
		if (typeof(T) == typeof(VLenType))
		{
			fixed (VLenType* vLenTypeTerm = (VLenType[])(object)buffer)
			{
				Execute((nint)vLenTypeTerm);
			}
			return;
		}
		throw new NotImplementedException();
	}

	protected unsafe void PinPointer<T>(T[,] buffer, Action<nint> Execute)
	{
		if (typeof(T) == typeof(byte))
		{
			fixed (byte* localValue = (byte[,])(object)buffer)
			{
				void* localValue2 = localValue;
				Execute((nint)localValue2);
			}
			return;
		}
		if (typeof(T) == typeof(float))
		{
			fixed (float* localValue3 = (float[,])(object)buffer)
			{
				void* localValue4 = localValue3;
				Execute((nint)localValue4);
			}
			return;
		}
		if (typeof(T) == typeof(double))
		{
			fixed (double* localValue5 = (double[,])(object)buffer)
			{
				void* localValue6 = localValue5;
				Execute((nint)localValue6);
			}
			return;
		}
		if (typeof(T) == typeof(int))
		{
			fixed (int* localValue7 = (int[,])(object)buffer)
			{
				void* localValue8 = localValue7;
				Execute((nint)localValue8);
			}
			return;
		}
		if (typeof(T) == typeof(long))
		{
			fixed (long* localValue9 = (long[,])(object)buffer)
			{
				void* localValue10 = localValue9;
				Execute((nint)localValue10);
			}
			return;
		}
		throw new NotImplementedException();
	}

	protected unsafe void PinPointer<T>(T[,,] buffer, Action<nint> Execute)
	{
		if (typeof(T) == typeof(byte))
		{
			fixed (byte* localValue = (byte[,,])(object)buffer)
			{
				void* localValue2 = localValue;
				Execute((nint)localValue2);
			}
			return;
		}
		if (typeof(T) == typeof(float))
		{
			fixed (float* localValue3 = (float[,,])(object)buffer)
			{
				void* localValue4 = localValue3;
				Execute((nint)localValue4);
			}
			return;
		}
		if (typeof(T) == typeof(double))
		{
			fixed (double* localValue5 = (double[,,])(object)buffer)
			{
				void* localValue6 = localValue5;
				Execute((nint)localValue6);
			}
			return;
		}
		if (typeof(T) == typeof(int))
		{
			fixed (int* localValue7 = (int[,,])(object)buffer)
			{
				void* localValue8 = localValue7;
				Execute((nint)localValue8);
			}
			return;
		}
		if (typeof(T) == typeof(long))
		{
			fixed (long* localValue9 = (long[,,])(object)buffer)
			{
				void* localValue10 = localValue9;
				Execute((nint)localValue10);
			}
			return;
		}
		throw new NotImplementedException();
	}

	private void ParseString<T>(ulong[] buffDims, byte[] byteValues, int strLength, T[] values)
	{
		string[] values2 = (string[])(object)values;
		ParseString(buffDims, byteValues, strLength, values2);
	}

	private void ParseString<T>(ulong[] size, byte[,] byteValues, int strLength, T[,] values)
	{
		string[,] values2 = (string[,])(object)values;
		ParseString(size, byteValues, strLength, values2);
	}

	private void ParseString<T>(ulong[] size, byte[,,] byteValues, int strLength, T[,,] values)
	{
		string[,,] values2 = (string[,,])(object)values;
		ParseString(size, byteValues, strLength, values2);
	}

	private void ParseString(ulong[] buffDims, byte[] byteValues, int strLength, string[] values)
	{
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		for (int i = 0; i <= Convert.ToInt32(buffDims[0]) - 1; i++)
		{
			byte[] byteValuesArray = new byte[strLength];
			int j;
			for (j = 0; j < strLength && byteValues[i * strLength + j] != 0; j++)
			{
				byteValuesArray[j] = byteValues[i * strLength + j];
			}
			values[i] = aSCIIEncoding.GetString(byteValuesArray, 0, j);
		}
	}

	private void ParseString(ulong[] size, byte[,] byteValues, int strLength, string[,] values)
	{
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		for (int i = 0; i < Convert.ToInt32(size[0]); i++)
		{
			for (int j = 0; j < Convert.ToInt32(size[1]); j++)
			{
				byte[] byteValuesArray = new byte[strLength];
				int k;
				for (k = 0; k < strLength && byteValues[i, j * strLength + k] != 0; k++)
				{
					byteValuesArray[k] = byteValues[i, j * strLength + k];
				}
				values[i, j] = aSCIIEncoding.GetString(byteValuesArray, 0, k);
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

	protected ulong[] GetDimensions(long spaceId)
	{
		ulong[] ulongs = new ulong[H5S.get_simple_extent_ndims(spaceId)];
		H5S.get_simple_extent_dims(spaceId, ulongs, null);
		return ulongs;
	}

	protected unsafe void GetDimensions(long spaceId, ulong* dims)
	{
		H5S.get_simple_extent_dims(spaceId, dims, null);
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
				values = new T[Convert.ToInt32(size[0])];
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
		if (size.GetLength(0) != 2)
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
		if (size.GetLength(0) != 3)
		{
			throw new Exception("Requested array is dimensioned incorrectly.");
		}
		if (values == null || values.GetLength(0) != (int)size[0] || values.GetLength(1) != (int)size[1] || values.GetLength(2) != (int)size[2])
		{
			values = new T[Convert.ToInt32(size[0]), Convert.ToInt32(size[1]), Convert.ToInt32(size[2])];
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
			values = new T[Convert.ToInt32(size[0]), Convert.ToInt32(size[1]), Convert.ToInt32(size[2])];
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

	public void CopyTo(H5Writer writer, string sourceObject, string destinationObject)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
		if (writer.ExistsAsGroup(destinationObject) || writer.ExistsAsDataset(destinationObject))
		{
			writer.Delete(destinationObject);
		}
		H5O.copy(FileId, sourceObject, writer.FileId, destinationObject, 0L, 0L);
	}

	public void CopyAttributesTo(H5Writer writer, string sourceObject, string destinationObject)
	{
		_ = _log & Log.RecordOperation;
		_ = 0;
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
				Transfer<string>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(int))
			{
				Transfer<int>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(long))
			{
				Transfer<long>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(float))
			{
				Transfer<float>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(double))
			{
				Transfer<double>(this, writer, sourceObject, destinationObject, key, nDims);
				continue;
			}
			throw new NotImplementedException();
		}
	}

	protected static void Transfer<T>(H5Reader h5r, H5Writer h5w, string fromObject, string toObject, string name, int nDims)
	{
		switch (nDims)
		{
		case 0:
		{
			T localValue = default;
			h5r.ReadAttributeScalar(fromObject, name, ref localValue);
			h5w.WriteAttribute(toObject, name, localValue);
			break;
		}
		case 1:
		{
			T[] values3 = null;
			h5r.ReadAttribute(fromObject, name, ref values3);
			h5w.WriteAttribute(toObject, name, values3);
			break;
		}
		case 2:
		{
			T[,] values2 = null;
			h5r.ReadAttribute(fromObject, name, ref values2);
			h5w.WriteAttribute(toObject, name, values2);
			break;
		}
		case 3:
		{
			T[,,] values = null;
			h5r.ReadAttribute(fromObject, name, ref values);
			h5w.WriteAttribute(toObject, name, values);
			break;
		}
		default:
			throw new NotImplementedException();
		}
	}

	public void TransposeToColumnChunkingVB<T>(string sourceDataset, H5Writer writer, string destDataset, int destChunkWidth, ProgressReporter prog = null) where T : struct
	{
		if (typeof(T) == typeof(float))
		{
			TransposeToColumnChunking<float>(sourceDataset, writer, destDataset, destChunkWidth, prog);
			return;
		}
		throw new NotImplementedException();
	}

	public void TransposeToColumnChunking<T>(string sourceDataset, H5Writer writer, string destDataset, int destChunkWidth, ProgressReporter prog = null) where T : unmanaged
	{
		if (!ExistsAsDataset(sourceDataset))
		{
			throw new ObjectNotFoundException(sourceDataset, "TransposeToColumnChunking");
		}
		bool isSafetyChecksOn = IsSafetyChecksOn;
		SetSafetyChecks(boolValue: false);
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		object progressLock = new object();
		int is64BitProcessEnvironmentTerm = ((!Environment.Is64BitProcess) ? 5242880 : 10485760);
		ParallelOptions parallelOptions = new ParallelOptions();
		parallelOptions.MaxDegreeOfParallelism = Environment.ProcessorCount * 8;
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
		if ((long)srcHeight * (long)srcWidth * sizeOfT <= is64BitProcessEnvironmentTerm)
		{
			T[,] values = null;
			ReadDataset(sourceDataset, ref values);
			writer.Create2dExtendibleDataset<T>(destDataset, destChunkHeight, destChunkWidth);
			writer.AddColumns(destDataset, values);
			prog.ReportProgress(100);
			return;
		}
		int tmpChunkHeight = (int)Math.Ceiling((double)is64BitProcessEnvironmentTerm / (double)(sizeOfT * srcWidth));
		int tmpChunkWidth = (int)Math.Ceiling((double)is64BitProcessEnvironmentTerm / (double)(sizeOfT * srcHeight));
		int srcChunksPerTempChunk = (int)Math.Ceiling((double)tmpChunkHeight / (double)srcChunkHeight);
		int destChunksPerTempChunk = (int)Math.Ceiling((double)tmpChunkWidth / (double)destChunkWidth);
		tmpChunkHeight = srcChunkHeight * srcChunksPerTempChunk;
		tmpChunkWidth = destChunkWidth * destChunksPerTempChunk;
		int tmpChunksTall = (int)Math.Ceiling((double)srcHeight / (double)tmpChunkHeight);
		int tmpChunksWide = (int)Math.Ceiling((double)srcWidth / (double)tmpChunkWidth);
		string combinePathTerm = Path.Combine(Path.GetTempPath(), "HDF_TEMP", Path.GetRandomFileName()) + ".h5";
		string datasetName = "/TEMP";
		try
		{
			H5Writer tempWriter = new H5Writer(combinePathTerm);
			try
			{
				long tmpWidth = tmpChunkWidth * tmpChunksWide;
				long tmpHeight = tmpChunkHeight * tmpChunksTall;
				long[] newDims = new long[2] { tmpHeight, tmpWidth };
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
						tempWriter.SetExtent(writeDset, newDims);
						int transferBlocksTall = tmpChunksTall;
						int transferBlocksCompleted = 0;
						Parallel.For(0, transferBlocksTall, parallelOptions, (int blockIdx) =>
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
								tempWriter.WriteChunkDirect_Threadsafe(writeDset, intValuesArray, tlWriteBufferTerm);
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
						Parallel.For(0, transferBlocksWide, parallelOptions, (int blockIdx) =>
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
								writer.WriteChunkDirect_Threadsafe(writeDset2, intValues3, tlWriteBuffer2Term);
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
			SetSafetyChecks(isSafetyChecksOn);
		}
	}

	public static void TestDifferences(string f1, string f2)
	{
		using H5Reader h5r = new H5Reader(f1);
		using H5Reader h5r2 = new H5Reader(f2);
		RecursiveCheckFolder("/", h5r, h5r2);
	}

	private static void RecursiveCheckFolder(string folder, H5Reader h5r1, H5Reader h5r2)
	{
		foreach (string groupName in h5r1.GetGroupNames(folder))
		{
			string folder2 = folder + "/" + groupName;
			RecursiveCheckFolder(folder2, h5r1, h5r2);
			CheckSubDsets(folder2, h5r1, h5r2);
		}
	}

	private static void CheckSubDsets(string folder, H5Reader h5r1, H5Reader h5r2)
	{
		foreach (string datasetName in h5r1.GetDatasetNames(folder))
		{
			string folderDatasetNameTerm = folder + "/" + datasetName;
			if (!h5r2.ExistsAsDataset(folderDatasetNameTerm))
			{
				throw new Exception("it should exist!");
			}
			if (h5r1.GetDatasetType(folderDatasetNameTerm) == typeof(float) && h5r1.GetDatasetNDims(folderDatasetNameTerm) == 1)
			{
				float[] values = null;
				float[] values2 = null;
				h5r1.ReadDataset(folderDatasetNameTerm, ref values);
				h5r2.ReadDataset(folderDatasetNameTerm, ref values2);
				for (int i = 0; i < values.GetLength(0); i++)
				{
					if (values[i] != values2[i] && (!float.IsNaN(values[i]) || !float.IsNaN(values2[i])))
					{
						throw new Exception("err");
					}
				}
			}
			else if (h5r1.GetDatasetType(folderDatasetNameTerm) == typeof(float) && h5r1.GetDatasetNDims(folderDatasetNameTerm) == 2)
			{
				float[,] values3 = null;
				float[,] values4 = null;
				h5r1.ReadDataset(folderDatasetNameTerm, ref values3);
				h5r2.ReadDataset(folderDatasetNameTerm, ref values4);
				for (int j = 0; j < values3.GetLength(0); j++)
				{
					for (int k = 0; k < values3.GetLength(1); k++)
					{
						if (values3[j, k] != values4[j, k])
						{
							throw new Exception("err");
						}
					}
				}
			}
			else if (h5r1.GetDatasetType(folderDatasetNameTerm) == typeof(double) && h5r1.GetDatasetNDims(folderDatasetNameTerm) == 1)
			{
				double[] values5 = null;
				double[] values6 = null;
				h5r1.ReadDataset(folderDatasetNameTerm, ref values5);
				h5r2.ReadDataset(folderDatasetNameTerm, ref values6);
				for (int l = 0; l <= values5.GetLength(0) - 1; l++)
				{
					if (values5[l] != values6[l])
					{
						throw new Exception("err");
					}
				}
			}
			else if (h5r1.GetDatasetType(folderDatasetNameTerm) == typeof(double) && h5r1.GetDatasetNDims(folderDatasetNameTerm) == 2)
			{
				double[,] values7 = null;
				double[,] values8 = null;
				h5r1.ReadDataset(folderDatasetNameTerm, ref values7);
				h5r2.ReadDataset(folderDatasetNameTerm, ref values8);
				for (int m = 0; m <= values7.GetLength(0) - 1; m++)
				{
					for (int n = 0; n <= values7.GetLength(1) - 1; n++)
					{
						if (values7[m, n] != values8[m, n])
						{
							throw new Exception("err");
						}
					}
				}
			}
			else if (h5r1.GetDatasetType(folderDatasetNameTerm) == typeof(int) && h5r1.GetDatasetNDims(folderDatasetNameTerm) == 1)
			{
				int[] values9 = null;
				int[] values10 = null;
				h5r1.ReadDataset(folderDatasetNameTerm, ref values9);
				h5r2.ReadDataset(folderDatasetNameTerm, ref values10);
				for (int intervalIndex = 0; intervalIndex <= values9.GetLength(0) - 1; intervalIndex++)
				{
					if (values9[intervalIndex] != values10[intervalIndex])
					{
						throw new Exception("err");
					}
				}
			}
			else
			{
				if (!(h5r1.GetDatasetType(folderDatasetNameTerm) == typeof(int)) || h5r1.GetDatasetNDims(folderDatasetNameTerm) != 2)
				{
					continue;
				}
				int[,] values11 = null;
				int[,] values12 = null;
				h5r1.ReadDataset(folderDatasetNameTerm, ref values11);
				h5r2.ReadDataset(folderDatasetNameTerm, ref values12);
				for (int intervalIndex2 = 0; intervalIndex2 <= values11.GetLength(0) - 1; intervalIndex2++)
				{
					for (int intervalIndex3 = 0; intervalIndex3 <= values11.GetLength(1) - 1; intervalIndex3++)
					{
						if (values11[intervalIndex2, intervalIndex3] != values12[intervalIndex2, intervalIndex3])
						{
							throw new Exception("err");
						}
					}
				}
			}
		}
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
}
