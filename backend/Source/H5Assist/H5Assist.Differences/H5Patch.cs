using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using H5Assist.Chunking;
using H5Assist.Identifiers;
using HDF.PInvoke;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace H5Assist.Differences;

public static class H5Patch
{
	private static readonly ILogger _logger = LogHelper.GetLogger("H5Patch");

	public const string BaseGroupName = "/__HASH";

	public const string PatchAttribute = "__PATCH";

	private const char Delimiter = '|';

	private const int HashSize = 8;

	private static readonly HashSet<Type> SupportedTypes = new HashSet<Type>
	{
		typeof(float),
		typeof(double),
		typeof(int),
		typeof(long),
		typeof(byte),
		typeof(short),
		typeof(bool)
	};

	public static List<ExtractedDataset> Scan(string inputFile, DiffOptions options = null)
	{
		if (options == null)
		{
			options = new DiffOptions();
		}
		List<string> stringList = new List<string>();
		using H5Reader reader = new H5Reader(inputFile);
		ScanRecursive(reader, "/", options, stringList);
		List<ExtractedDataset> extractedDatasets = new List<ExtractedDataset>();
		foreach (string textValue in stringList)
		{
			long onDiskBytes = ComputeOnDiskSize(reader, textValue);
			extractedDatasets.Add(new ExtractedDataset(textValue, "", onDiskBytes));
		}
		return extractedDatasets;
	}

	public static PatchCreationLog CreatePatch(string inputFile, string baseFile, string patchFile, DiffOptions options = null)
	{
		if (options == null)
		{
			options = new DiffOptions();
		}
		PatchCreationLog patchCreationLog = new PatchCreationLog();
		using H5Reader h5Reader = new H5Reader(inputFile);
		using H5Writer h5Writer = new H5Writer(baseFile);
		using H5Writer h5Writer2 = new H5Writer(patchFile);
		List<string> stringList = new List<string>();
		ScanRecursive(h5Reader, "/", options, stringList);
		Dictionary<string, string> stringByString = new Dictionary<string, string>();
		foreach (string textValue in stringList)
		{
			string computeDatasetHashH5ReaderTerm = (stringByString[textValue] = ComputeDatasetHash(h5Reader, textValue));
			long onDiskSize = ComputeOnDiskSize(h5Reader, textValue);
			string textValue2 = "/__HASH/" + computeDatasetHashH5ReaderTerm;
			if (!h5Writer.ExistsAsDataset(textValue2))
			{
				h5Writer.EnsureParentGroup(textValue2);
				h5Reader.CopyTo(h5Writer, textValue, textValue2);
				foreach (string attributeName in h5Writer.GetAttributeNames(textValue2))
				{
					h5Writer.DeleteAttribute(textValue2, attributeName);
				}
				h5Writer.WriteAttribute(textValue2, "Date Ingested", DateTime.UtcNow.ToString("O"));
				h5Writer.WriteAttribute(textValue2, "Original Path", textValue);
				patchCreationLog.NewBaseDatasets++;
			}
			else
			{
				patchCreationLog.DeduplicatedDatasets++;
			}
			patchCreationLog.Extracted.Add(new ExtractedDataset(textValue, computeDatasetHashH5ReaderTerm, onDiskSize));
			patchCreationLog.TotalExtractedBytes += onDiskSize;
		}
		HashSet<string> extractedSet = new HashSet<string>(stringByString.Keys);
		CopyToPatchRecursive(h5Reader, h5Writer2, "/", extractedSet, patchCreationLog);
		string[] strings = new string[stringByString.Count];
		int ordinal = 0;
		foreach (KeyValuePair<string, string> localValue in stringByString)
		{
			strings[ordinal++] = $"{localValue.Key}{'|'}{localValue.Value}";
		}
		h5Writer2.WriteAttribute("/", "__PATCH", strings);
		return patchCreationLog;
	}

	public static RestoreLog Restore(string baseFile, string patchFile, string outputFile)
	{
		RestoreLog restoreLog = new RestoreLog();
		using H5Reader baseReader = new H5Reader(baseFile);
		using H5Reader h5Reader = new H5Reader(patchFile);
		using H5Writer output = new H5Writer(outputFile);
		h5Reader.ReadAttribute("/", "__PATCH", out string[] values);
		Dictionary<string, string> stringByString = new Dictionary<string, string>();
		HashSet<string> stringSet = new HashSet<string>();
		string[] strings = values;
		foreach (string textValue in strings)
		{
			int lastIndexOf = textValue.LastIndexOf('|');
			string substring = textValue.Substring(0, lastIndexOf);
			string substring2 = textValue.Substring(lastIndexOf + 1);
			stringByString[substring] = substring2;
			stringSet.Add(substring);
		}
		RestoreRecursive(h5Reader, baseReader, output, "/", stringByString, stringSet, restoreLog);
		CopyRootAttributes(h5Reader, output);
		return restoreLog;
	}

	private static void ScanRecursive(H5Reader reader, string groupPath, DiffOptions options, List<string> results)
	{
		foreach (string datasetName in reader.GetDatasetNames(groupPath))
		{
			string groupPathDatasetNameTerm = ((groupPath == "/") ? ("/" + datasetName) : (groupPath + "/" + datasetName));
			if ((options.AllowedPaths.Count <= 0 || IsUnderAllowedPath(groupPathDatasetNameTerm, options.AllowedPaths)) && IsExtractable(reader, groupPathDatasetNameTerm, options))
			{
				results.Add(groupPathDatasetNameTerm);
			}
		}
		foreach (string groupName in reader.GetGroupNames(groupPath))
		{
			string groupPathGroupNameTerm = ((groupPath == "/") ? ("/" + groupName) : (groupPath + "/" + groupName));
			if (options.AllowedPaths.Count <= 0 || IsUnderOrAboveAllowedPath(groupPathGroupNameTerm, options.AllowedPaths))
			{
				ScanRecursive(reader, groupPathGroupNameTerm, options, results);
			}
		}
	}

	private static bool IsUnderAllowedPath(string path, List<string> allowedPaths)
	{
		foreach (string allowedPath in allowedPaths)
		{
			if (path.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsUnderOrAboveAllowedPath(string path, List<string> allowedPaths)
	{
		foreach (string allowedPath in allowedPaths)
		{
			if (path.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase) || allowedPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsExtractable(H5Reader reader, string path, DiffOptions options)
	{
		try
		{
			if (reader.DatasetIsVariableLength(path))
			{
				return false;
			}
			Type datasetType = reader.GetDatasetType(path);
			if (!SupportedTypes.Contains(datasetType))
			{
				return false;
			}
			return ComputeOnDiskSize(reader, path) >= options.MinCompressedSizeBytes;
		}
		catch (Exception ex)
		{
			_logger.LogWarning("Skipping dataset {Path} during scan: {Error}", path, ex.Message);
			return false;
		}
	}

	private static long ComputeOnDiskSize(H5Reader reader, string path)
	{
		if (reader.GetChunkSize(path).Length != 0)
		{
			return reader.CompressedSize(path);
		}
		long[] datasetDimensions = reader.GetDatasetDimensions(path);
		long unity = 1L;
		long[] datasetDimensionsArray = datasetDimensions;
		foreach (long datasetDimension in datasetDimensionsArray)
		{
			unity *= datasetDimension;
		}
		int typeSize = GetTypeSize(reader.GetDatasetType(path));
		return unity * typeSize;
	}

	public static string ComputeDatasetHash(H5Reader reader, string path)
	{
		long[] chunkSize = reader.GetChunkSize(path);
		byte[] inArray = ((chunkSize.Length == 0) ? HashUnchunkedDataset(reader, path) : HashChunkedDataset(reader, path, chunkSize));
		return Convert.ToHexString(inArray);
	}

	private unsafe static byte[] HashChunkedDataset(H5Reader reader, string path, long[] chunkSize)
	{
		long[] datasetDimensions = reader.GetDatasetDimensions(path);
		int datasetDimensionLength = datasetDimensions.Length;
		long[] longs = new long[datasetDimensionLength];
		for (int i = 0; i < datasetDimensionLength; i++)
		{
			longs[i] = (long)Math.Ceiling((double)datasetDimensions[i] / (double)chunkSize[i]);
		}
		long unity = 1L;
		for (int j = 0; j < datasetDimensionLength; j++)
		{
			unity *= longs[j];
		}
		byte[] byteValues = new byte[unity * 8];
		using (H5DatasetID h5DatasetID = reader.OpenDataset(path))
		{
			long[] longsArray = new long[datasetDimensionLength];
			ulong[] ulongs = new ulong[datasetDimensionLength];
			int ordinal = 0;
			bool condition;
			do
			{
				for (int k = 0; k < datasetDimensionLength; k++)
				{
					ulongs[k] = (ulong)(longsArray[k] * chunkSize[k]);
				}
				ulong chunk_bytes = 0uL;
				H5D.get_chunk_storage_size(h5DatasetID.DatasetId, ulongs, ref chunk_bytes);
				if (chunk_bytes != 0)
				{
					byte[] byteValuesArray = new byte[chunk_bytes];
					uint filter_mask = 0u;
					fixed (byte* buf = byteValuesArray)
					{
						Advanced.read_chunk(h5DatasetID.DatasetId, 0L, ulongs, ref filter_mask, (nint)buf);
					}
					Span<byte> destination = byteValues.AsSpan(ordinal * 8, 8);
					XxHash3.Hash(byteValuesArray, destination, 0L);
				}
				ordinal++;
				condition = false;
				int datasetDimensionLengthOffset = datasetDimensionLength - 1;
				while (datasetDimensionLengthOffset >= 0)
				{
					if (longsArray[datasetDimensionLengthOffset] == longs[datasetDimensionLengthOffset] - 1)
					{
						if (datasetDimensionLengthOffset == 0)
						{
							condition = true;
							break;
						}
						datasetDimensionLengthOffset--;
						continue;
					}
					longsArray[datasetDimensionLengthOffset]++;
					for (int l = datasetDimensionLengthOffset + 1; l < datasetDimensionLength; l++)
					{
						longsArray[l] = 0L;
					}
					break;
				}
			}
			while (!condition);
		}
		byte[] byteValues2 = new byte[8];
		XxHash3.Hash(byteValues, byteValues2, 0L);
		return byteValues2;
	}

	private static byte[] HashUnchunkedDataset(H5Reader reader, string path)
	{
		Type datasetType = reader.GetDatasetType(path);
		byte[] byteValues = new byte[8];
		if (datasetType == typeof(float))
		{
			HashUnchunkedTyped<float>(reader, path, byteValues);
		}
		else if (datasetType == typeof(double))
		{
			HashUnchunkedTyped<double>(reader, path, byteValues);
		}
		else if (datasetType == typeof(int))
		{
			HashUnchunkedTyped<int>(reader, path, byteValues);
		}
		else if (datasetType == typeof(long))
		{
			HashUnchunkedTyped<long>(reader, path, byteValues);
		}
		else if (datasetType == typeof(byte))
		{
			HashUnchunkedTyped<byte>(reader, path, byteValues);
		}
		else if (datasetType == typeof(short))
		{
			HashUnchunkedTyped<short>(reader, path, byteValues);
		}
		else
		{
			if (!(datasetType == typeof(bool)))
			{
				throw new NotSupportedException("Unsupported dataset type: " + datasetType.Name);
			}
			HashUnchunkedTyped<bool>(reader, path, byteValues);
		}
		return byteValues;
	}

	private static void HashUnchunkedTyped<T>(H5Reader reader, string path, byte[] hash) where T : unmanaged
	{
		int datasetDimensionLength = reader.GetDatasetDimensions(path).Length;
		ReadOnlySpan<byte> source;
		switch (datasetDimensionLength)
		{
		case 1:
		{
			reader.ReadDataset(path, out T[] values3);
			source = MemoryMarshal.AsBytes(values3.AsSpan());
			break;
		}
		case 2:
		{
			reader.ReadDataset(path, out T[,] values2);
			source = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in values2[0, 0], values2.Length));
			break;
		}
		case 3:
		{
			reader.ReadDataset(path, out T[,,] values);
			source = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in values[0, 0, 0], values.Length));
			break;
		}
		default:
			throw new NotSupportedException($"Unsupported dataset rank: {datasetDimensionLength}");
		}
		XxHash3.Hash(source, hash, 0L);
	}

	private static void CopyToPatchRecursive(H5Reader input, H5Writer patch, string groupPath, HashSet<string> extractedSet, PatchCreationLog log)
	{
		foreach (string datasetName in input.GetDatasetNames(groupPath))
		{
			string groupPathDatasetNameTerm = ((groupPath == "/") ? ("/" + datasetName) : (groupPath + "/" + datasetName));
			if (extractedSet.Contains(groupPathDatasetNameTerm))
			{
				patch.CreateGroup(groupPathDatasetNameTerm);
				input.CopyAttributesTo(patch, groupPathDatasetNameTerm, groupPathDatasetNameTerm);
			}
			else
			{
				input.CopyTo(patch, groupPathDatasetNameTerm, groupPathDatasetNameTerm);
				log.DatasetsKeptInPatch++;
			}
		}
		foreach (string groupName in input.GetGroupNames(groupPath))
		{
			string groupPathGroupNameTerm = ((groupPath == "/") ? ("/" + groupName) : (groupPath + "/" + groupName));
			patch.CreateGroup(groupPathGroupNameTerm);
			input.CopyAttributesTo(patch, groupPathGroupNameTerm, groupPathGroupNameTerm);
			CopyToPatchRecursive(input, patch, groupPathGroupNameTerm, extractedSet, log);
		}
		if (groupPath == "/")
		{
			input.CopyAttributesTo(patch, "/", "/");
		}
	}

	private static void RestoreRecursive(H5Reader patch, H5Reader baseReader, H5Writer output, string groupPath, Dictionary<string, string> extractedMap, HashSet<string> extractedPaths, RestoreLog log)
	{
		foreach (string datasetName in patch.GetDatasetNames(groupPath))
		{
			string groupPathDatasetNameTerm = ((groupPath == "/") ? ("/" + datasetName) : (groupPath + "/" + datasetName));
			patch.CopyTo(output, groupPathDatasetNameTerm, groupPathDatasetNameTerm);
			log.DatasetsRestoredFromPatch++;
		}
		foreach (string groupName in patch.GetGroupNames(groupPath))
		{
			string groupPathGroupNameTerm = ((groupPath == "/") ? ("/" + groupName) : (groupPath + "/" + groupName));
			if (extractedPaths.Contains(groupPathGroupNameTerm))
			{
				string extractedMap2 = extractedMap[groupPathGroupNameTerm];
				string sourceObject = "/__HASH/" + extractedMap2;
				baseReader.CopyTo(output, sourceObject, groupPathGroupNameTerm);
				log.DatasetsRestoredFromBase++;
				foreach (string attributeName in output.GetAttributeNames(groupPathGroupNameTerm))
				{
					output.DeleteAttribute(groupPathGroupNameTerm, attributeName);
				}
				foreach (KeyValuePair<string, object> allAttribute in patch.ReadAllAttributes(groupPathGroupNameTerm))
				{
					output.WriteAttribute(groupPathGroupNameTerm, allAttribute.Key, allAttribute.Value);
				}
			}
			else
			{
				output.CreateGroup(groupPathGroupNameTerm);
				CopyAttributesFromPatch(patch, output, groupPathGroupNameTerm);
				RestoreRecursive(patch, baseReader, output, groupPathGroupNameTerm, extractedMap, extractedPaths, log);
			}
		}
	}

	private static void CopyRootAttributes(H5Reader patch, H5Writer output)
	{
		foreach (KeyValuePair<string, object> allAttribute in patch.ReadAllAttributes("/"))
		{
			if (!(allAttribute.Key == "__PATCH"))
			{
				output.WriteAttribute("/", allAttribute.Key, allAttribute.Value);
			}
		}
	}

	private static void CopyAttributesFromPatch(H5Reader patch, H5Writer output, string path)
	{
		foreach (KeyValuePair<string, object> allAttribute in patch.ReadAllAttributes(path))
		{
			output.WriteAttribute(path, allAttribute.Key, allAttribute.Value);
		}
	}

	private static int GetTypeSize(Type type)
	{
		if (type == typeof(float))
		{
			return 4;
		}
		if (type == typeof(double))
		{
			return 8;
		}
		if (type == typeof(int))
		{
			return 4;
		}
		if (type == typeof(long))
		{
			return 8;
		}
		if (type == typeof(byte))
		{
			return 1;
		}
		if (type == typeof(short))
		{
			return 2;
		}
		if (type == typeof(bool))
		{
			return 1;
		}
		throw new NotSupportedException("Unsupported type: " + type.Name);
	}
}
