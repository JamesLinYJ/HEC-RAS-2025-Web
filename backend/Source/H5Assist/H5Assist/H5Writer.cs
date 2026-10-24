using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance;
using H5Assist.Chunking;
using H5Assist.Identifiers;
using HDF.PInvoke;
using Microsoft.Extensions.Logging;
using Utility.Attributes;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Helpers;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;

namespace H5Assist;

public class H5Writer : H5Reader
{
	public bool UseCompression;

	public const int DefaultMaxChunkSizeBytes = 1048576;

	private static readonly ILogger _logger = H5Reader.Logger;

	private static bool _hasRegistered = false;

	private static long _fileAccessType;

	private static int _fileAccessRead;

	private static int _fileAccessWrite;

	private static string DescribeWriteBlocker(string fileName)
	{
		try
		{
			string directoryName = Path.GetDirectoryName(fileName);
			if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
			{
				return "Directory \"" + directoryName + "\" does not exist.";
			}
			if (!File.Exists(fileName))
			{
				return "File does not exist and could not be created - check permissions on the containing directory.";
			}
			if (File.GetAttributes(fileName).HasFlag(FileAttributes.ReadOnly))
			{
				return "File is marked read-only.";
			}
			try
			{
				using (File.Open(fileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				{
				}
			}
			catch (Exception ex)
			{
				return "File could not be opened exclusively: " + ex.Message;
			}
			return "File is readable but HDF5 rejected it - it may be corrupt, or already open elsewhere in this process.";
		}
		catch (Exception ex2)
		{
			return ex2.Message;
		}
	}

	public H5Writer(string fileName)
	{
		if (!File.Exists(fileName))
		{
			string directoryName = Path.GetDirectoryName(fileName);
			if (!Directory.Exists(directoryName) && !string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			long create = H5P.create(H5P.FILE_ACCESS);
			SetMinLibVer(create, H5F.libver_t.V110);
			FileId = H5F.create(fileName, 2u, 0L, create);
			if (FileId < 0)
			{
				H5P.close(create);
				throw new Exception(fileName + " could not be created. " + DescribeWriteBlocker(fileName));
			}
			H5P.close(create);
		}
		else
		{
			long create2 = H5P.create(H5P.FILE_ACCESS);
			SetMinLibVer(create2, H5F.libver_t.V110);
			FileId = H5F.open(fileName, 1u, 0L);
			if (FileId < 0)
			{
				H5P.close(create2);
				throw new Exception(fileName + " could not be opened for writing. " + DescribeWriteBlocker(fileName));
			}
			H5P.close(create2);
		}
		_filename = fileName;
		UseCompression = true;
		_logger.LogInformation("Opened {File} for writing.", fileName);
	}

	private H5Writer(string filename, bool useWritePermissions)
	{
		if (useWritePermissions)
		{
			if (!File.Exists(filename))
			{
				FileId = H5F.create(filename, 2u, 0L, 0L);
			}
			else
			{
				FileId = H5F.open(filename, 1u, 0L);
			}
		}
		else
		{
			if (!File.Exists(filename))
			{
				throw new FileNotFoundException("filename");
			}
			FileId = H5F.open(filename, 0u, 0L);
		}
		_filename = filename;
		UseCompression = true;
	}

	private H5Writer()
	{
	}

	public static H5Writer CreateInMemory(int bufferSizeIncrements = 1024, string identifier = "")
	{
		if (string.IsNullOrEmpty(identifier))
		{
			identifier = Guid.NewGuid().ToString();
		}
		if (bufferSizeIncrements <= 0)
		{
			throw new ArgumentOutOfRangeException("Bad value for bufferSizeIncrements");
		}
		long create = H5P.create(H5P.FILE_ACCESS);
		if (create < 0)
		{
			throw new Exception("Error creating File Access Property List");
		}
		SetMinLibVer(create, H5F.libver_t.V110);
		if (H5P.set_fapl_core(create, bufferSizeIncrements, 0u) < 0)
		{
			throw new Exception("Error setting File Access Property List");
		}
		long fileId = H5F.create(identifier, 2u, 0L, create);
		if (fileId < 0)
		{
			throw new Exception("Error creating in-memory file.");
		}
		H5P.close(create);
		return new H5Writer
		{
			_filename = identifier,
			UseCompression = true,
			FileId = fileId
		};
	}

	public void GetLibVerInfo(out string minVer, out string maxVer)
	{
		long get_access_plist = H5F.get_access_plist(FileId);
		H5F.libver_t libver_low = H5F.libver_t.EARLIEST;
		H5F.libver_t libver_high = H5F.libver_t.EARLIEST;
		H5P.get_libver_bounds(get_access_plist, ref libver_low, ref libver_high);
		minVer = libver_low.ToString();
		maxVer = libver_high.ToString();
		H5P.close(get_access_plist);
	}

	private static bool SetMinLibVer(long fapl, H5F.libver_t libver)
	{
		return H5P.set_libver_bounds(fapl, libver) == 0;
	}

	private static string GetErrors()
	{
		if ((int)H5E.get_num(H5E.get_current_stack()) == 0)
		{
			return string.Empty;
		}
		throw new NotImplementedException();
	}

	public static H5Writer OpenWithPermissions(string fileName, bool openReadWriteMode)
	{
		return new H5Writer(fileName, openReadWriteMode);
	}

	public static void Squeeze(string sourceFile, string destFile)
	{
		File.Delete(destFile);
		using H5Reader h5Reader = new H5Reader(sourceFile);
		using H5Writer writer = new H5Writer(destFile);
		foreach (string groupName in h5Reader.GetGroupNames("/"))
		{
			h5Reader.CopyTo(writer, groupName, groupName);
		}
		foreach (string datasetName in h5Reader.GetDatasetNames("/"))
		{
			h5Reader.CopyTo(writer, datasetName, datasetName);
		}
		h5Reader.CopyAttributesTo(writer, "/", "/");
	}

	public static void Squeeze(string filename)
	{
		string filename2 = Path.GetTempPath() + Guid.NewGuid().ToString() + ".h5";
		Squeeze(filename, filename2);
		File.Delete(filename);
		File.Copy(filename2, filename);
		File.Delete(filename2);
	}

	public static bool IsHDF5File(string filename)
	{
		if (H5F.is_hdf5(filename) > 0)
		{
			return true;
		}
		return false;
	}

	public void CreateGroup(string absolutePath)
	{
		string[] newStrings = absolutePath.Split('/');
		H5Reader.StripEmpty(ref newStrings);
		string path = "/";
		for (int i = 0; i < newStrings.Length; i++)
		{
			if (ExistsAsGroup(path + newStrings[i]))
			{
				path = path + newStrings[i] + "/";
				continue;
			}
			using (H5GroupID.Create(FileId, path + newStrings[i]))
			{
				path = path + newStrings[i] + "/";
			}
		}
	}

	public void EnsureParentGroup(string absolutePath)
	{
		string[] newStrings = absolutePath.Split('/');
		H5Reader.StripEmpty(ref newStrings);
		string path = "/";
		for (int i = 0; i < newStrings.Length - 1; i++)
		{
			if (ExistsAsGroup(path + newStrings[i]))
			{
				path = path + newStrings[i] + "/";
				continue;
			}
			using (H5GroupID.Create(FileId, path + newStrings[i]))
			{
				path = path + newStrings[i] + "/";
			}
		}
	}

	public void CreateExternalLink(string externFile, string externPath, string localPath)
	{
		if (H5L.create_external(externFile, externPath, FileId, localPath, 0L, 0L) < 0)
		{
			throw new Exception("Failed to create external link.");
		}
	}

	public void RenameGroup(string currentGroupName, string newGroupName)
	{
		if (H5L.move(FileId, currentGroupName, FileId, newGroupName, 0L, 0L) < 0)
		{
			throw new Exception($"Failed to rename group '{currentGroupName}' to '{newGroupName}'.");
		}
	}

	public void Delete(string groupOrDatasetName)
	{
		if (groupOrDatasetName == "/")
		{
			CleanGroup(groupOrDatasetName);
		}
		else if (ObjectExists(groupOrDatasetName) && H5L.delete(FileId, groupOrDatasetName, 0L) < 0)
		{
			throw new Exception("Failed to delete '" + groupOrDatasetName + "'.");
		}
	}

	public void CleanGroup(string groupName, bool deleteAttrs = true)
	{
		if (!ExistsAsGroup(groupName))
		{
			return;
		}
		foreach (string groupName2 in GetGroupNames(groupName))
		{
			Delete(groupName + "/" + groupName2);
		}
		foreach (string datasetName in GetDatasetNames(groupName))
		{
			Delete(groupName + "/" + datasetName);
		}
		if (deleteAttrs)
		{
			DeleteAllAttributes(groupName);
		}
	}

	public void CleanGroup(string groupName, IList<string> keepList)
	{
		if (!ExistsAsGroup(groupName))
		{
			return;
		}
		foreach (string groupName2 in GetGroupNames(groupName))
		{
			if (!keepList.Contains(groupName2))
			{
				Delete(groupName + "/" + groupName2);
			}
		}
		foreach (string datasetName in GetDatasetNames(groupName))
		{
			if (!keepList.Contains(datasetName))
			{
				Delete(groupName + "/" + datasetName);
			}
		}
	}

	private void DeleteAttributeIfExists(long objectId, string attributeName)
	{
		if (H5A.exists(objectId, attributeName) > 0)
		{
			H5A.delete(objectId, attributeName);
		}
	}

	private void WriteAttributePreCheck(string objectName, string attributeName)
	{
		EnsureObjectExists(objectName, "WriteAttributePreCheck");
	}

	public void WriteAttribute<T>(string objectName, string attributeName, T localValue)
	{
		WriteAttributePreCheck(objectName, attributeName);
		using H5ObjectID h5ObjectID = new H5ObjectID(FileId, objectName);
		DeleteAttributeIfExists(h5ObjectID.ObjectId, attributeName);
		bool condition = typeof(T) == typeof(string);
		if (localValue == null)
		{
			localValue = (T)(object)string.Empty;
		}
		int ordinal = 0;
		if (condition)
		{
			ordinal = Encoding.UTF8.GetByteCount((string)(object)localValue);
			if (ordinal == 0)
			{
				ordinal = 1;
			}
		}
		using H5AttributeID attr = H5AttributeID.CreateScalar(h5ObjectID.ObjectId, attributeName, typeof(T), ordinal);
		if (condition)
		{
			byte[] bytes = Encoding.UTF8.GetBytes((string)(object)localValue);
			if (bytes.Length == 0)
			{
				bytes = new byte[1];
			}
			H5AWriteInternal(attr, bytes);
		}
		else
		{
			H5AWriteInternal(attr, new T[1] { localValue });
		}
	}

	public void WriteAttribute(string objectName, string attributeName, object localValue2)
	{
		if (localValue2 == null)
		{
			return;
		}
		if (!(localValue2 is byte localValue))
		{
			if (!(localValue2 is short ordinal))
			{
				if (!(localValue2 is int ordinal2))
				{
					if (!(localValue2 is long ordinal3))
					{
						if (!(localValue2 is float computed))
						{
							if (!(localValue2 is double computed2))
							{
								if (!(localValue2 is bool condition))
								{
									if (!(localValue2 is string textValue))
									{
										if (!(localValue2 is byte[] values))
										{
											if (!(localValue2 is short[] values2))
											{
												if (!(localValue2 is int[] values3))
												{
													if (!(localValue2 is long[] values4))
													{
														if (!(localValue2 is float[] values5))
														{
															if (!(localValue2 is double[] values6))
															{
																if (!(localValue2 is bool[] values7))
																{
																	if (!(localValue2 is string[] values8))
																	{
																		if (!(localValue2 is byte[,] values9))
																		{
																			if (!(localValue2 is short[,] values10))
																			{
																				if (!(localValue2 is int[,] values11))
																				{
																					if (!(localValue2 is long[,] values12))
																					{
																						if (!(localValue2 is float[,] values13))
																						{
																							if (!(localValue2 is double[,] values14))
																							{
																								if (!(localValue2 is bool[,] values15))
																								{
																									if (localValue2 is string[,] values16)
																									{
																										WriteAttribute(objectName, attributeName, values16);
																										return;
																									}
																									_logger.LogWarning("Skipping attribute {AttributeName} of unsupported type {AttributeType} on {ObjectName}.", attributeName, localValue2.GetType().Name, objectName);
																								}
																								else
																								{
																									WriteAttribute(objectName, attributeName, values15);
																								}
																							}
																							else
																							{
																								WriteAttribute(objectName, attributeName, values14);
																							}
																						}
																						else
																						{
																							WriteAttribute(objectName, attributeName, values13);
																						}
																					}
																					else
																					{
																						WriteAttribute(objectName, attributeName, values12);
																					}
																				}
																				else
																				{
																					WriteAttribute(objectName, attributeName, values11);
																				}
																			}
																			else
																			{
																				WriteAttribute(objectName, attributeName, values10);
																			}
																		}
																		else
																		{
																			WriteAttribute(objectName, attributeName, values9);
																		}
																	}
																	else
																	{
																		WriteAttribute(objectName, attributeName, values8);
																	}
																}
																else
																{
																	WriteAttribute(objectName, attributeName, values7);
																}
															}
															else
															{
																WriteAttribute(objectName, attributeName, values6);
															}
														}
														else
														{
															WriteAttribute(objectName, attributeName, values5);
														}
													}
													else
													{
														WriteAttribute(objectName, attributeName, values4);
													}
												}
												else
												{
													WriteAttribute(objectName, attributeName, values3);
												}
											}
											else
											{
												WriteAttribute(objectName, attributeName, values2);
											}
										}
										else
										{
											WriteAttribute(objectName, attributeName, values);
										}
									}
									else
									{
										WriteAttribute(objectName, attributeName, textValue);
									}
								}
								else
								{
									WriteAttribute(objectName, attributeName, condition);
								}
							}
							else
							{
								WriteAttribute(objectName, attributeName, computed2);
							}
						}
						else
						{
							WriteAttribute(objectName, attributeName, computed);
						}
					}
					else
					{
						WriteAttribute(objectName, attributeName, ordinal3);
					}
				}
				else
				{
					WriteAttribute(objectName, attributeName, ordinal2);
				}
			}
			else
			{
				WriteAttribute(objectName, attributeName, ordinal);
			}
		}
		else
		{
			WriteAttribute(objectName, attributeName, localValue);
		}
	}

	public void WriteAttribute<T>(string objectName, string attributeName, T[] values)
	{
		WriteAttributePreCheck(objectName, attributeName);
		using H5ObjectID h5ObjectID = new H5ObjectID(FileId, objectName);
		DeleteAttributeIfExists(h5ObjectID.ObjectId, attributeName);
		ulong[] size = new ulong[1] { (ulong)values.Length };
		bool condition = typeof(T) == typeof(string);
		int maxStringSize = 0;
		if (condition)
		{
			maxStringSize = GetMaxStringSize((string[])(object)values);
		}
		using H5AttributeID attr = H5AttributeID.Create(h5ObjectID.ObjectId, attributeName, typeof(T), size, maxStringSize);
		if (condition)
		{
			H5AWriteInternal(attr, SerializeAligned((string[])(object)values, maxStringSize));
		}
		else
		{
			H5AWriteInternal(attr, values);
		}
	}

	public void WriteAttribute<T>(string objectName, string attributeName, T[,] values)
	{
		WriteAttributePreCheck(objectName, attributeName);
		using H5ObjectID h5ObjectID = new H5ObjectID(FileId, objectName);
		DeleteAttributeIfExists(h5ObjectID.ObjectId, attributeName);
		ulong[] size = new ulong[2]
		{
			(ulong)values.GetLength(0),
			(ulong)values.GetLength(1)
		};
		bool condition = typeof(T) == typeof(string);
		int setSize = 0;
		if (condition)
		{
			setSize = GetMaxStringSize((string[,])(object)values);
		}
		using H5AttributeID attr = H5AttributeID.Create(h5ObjectID.ObjectId, attributeName, typeof(T), size, setSize);
		if (condition)
		{
			H5AWriteInternal(attr, SerializeAligned((string[,])(object)values, setSize));
		}
		else
		{
			H5AWriteInternal(attr, values);
		}
	}

	public void WriteEnum<T>(string objectName, string attributeName, T localValue) where T : struct, Enum
	{
		string serialize = DiskNameAttribute.Serialize(localValue);
		if (string.IsNullOrEmpty(serialize))
		{
			throw new ArgumentException(localValue.ToString() + " does not appear to implement the DiskNameAttribute attribute for serialization.");
		}
		WriteAttribute(objectName, attributeName, serialize);
	}

	public void DeleteAttribute(string objectName, string attributeName)
	{
		using H5ObjectID h5ObjectID = new H5ObjectID(FileId, objectName);
		DeleteAttributeIfExists(h5ObjectID.ObjectId, attributeName);
	}

	public void DeleteAllAttributes(string objectName)
	{
		using H5ObjectID h5ObjectID = new H5ObjectID(FileId, objectName);
		foreach (string attributeName in GetAttributeNames(objectName))
		{
			DeleteAttributeIfExists(h5ObjectID.ObjectId, attributeName);
		}
	}

	private unsafe int H5AWriteInternal<T>(long attributeId, long typeId, T[] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5A.write(attributeId, typeId, (nint)memoryHandle.Pointer);
	}

	private unsafe int H5AWriteInternal<T>(long attributeId, long typeId, T[,] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5A.write(attributeId, typeId, (nint)memoryHandle.Pointer);
	}

	private int H5AWriteInternal<T>(H5AttributeID attr, T[] buffer)
	{
		return H5AWriteInternal(attr.AttributeId, attr.DataType.TypeId, buffer);
	}

	private int H5AWriteInternal<T>(H5AttributeID attr, T[,] buffer)
	{
		return H5AWriteInternal(attr.AttributeId, attr.DataType.TypeId, buffer);
	}

	private void WriteDatasetPreCheck(string datasetName)
	{
		CreateGroup(GetOneLevelUp(datasetName));
		if (ExistsAsDataset(datasetName) || ExistsAsGroup(datasetName))
		{
			Delete(datasetName);
		}
	}

	private void LogDatasetWrite(string datasetName, int elementCount, long startTicks)
	{
		TimeSpan elapsedTime = Stopwatch.GetElapsedTime(startTicks);
		_logger.LogInformation("Wrote {Dataset} to {File}: {Elements} elements, compressed {Compressed}, in {Elapsed}", datasetName, _filename, elementCount, UseCompression, elapsedTime);
	}

	public void WriteDataset<T>(string datasetName, T[] values) where T : unmanaged
	{
		long timestamp = Stopwatch.GetTimestamp();
		WriteDatasetPreCheck(datasetName);
		ulong[] size = new ulong[1] { (ulong)values.Length };
		ulong[] chunking = OptimalChunking(values);
		using (H5DatasetID dataset = H5DatasetID.CreateBasic(FileId, datasetName, typeof(T), size, chunking))
		{
			if (values.Length == 0)
			{
				return;
			}
			H5DWriteInternal(dataset, values);
		}
		LogDatasetWrite(datasetName, values.Length, timestamp);
	}

	public void WriteDataset<T>(string datasetName, Memory<T> values) where T : unmanaged
	{
		WriteDatasetPreCheck(datasetName);
		ulong[] size = new ulong[1] { (ulong)values.Length };
		ulong[] chunking = OptimalChunking(values.Span);
		using H5DatasetID dataset = H5DatasetID.CreateBasic(FileId, datasetName, typeof(T), size, chunking);
		if (values.Length != 0)
		{
			H5DWriteInternal(dataset, values);
		}
	}

	public void WriteDataset(string datasetName, string[] values)
	{
		WriteDatasetPreCheck(datasetName);
		ulong[] size = new ulong[1] { (ulong)values.Length };
		ulong[] chunking = OptimalChunking(values);
		int maxStringSize = GetMaxStringSize(values);
		using H5DatasetID dataset = H5DatasetID.CreateBasic(FileId, datasetName, typeof(string), size, chunking, maxStringSize);
		if (values.Length != 0)
		{
			H5DWriteInternal(dataset, SerializeAligned(values, maxStringSize));
		}
	}

	public void WriteMessageLog(string datasetPrefix, IReadOnlyList<MessageLogEntry> entries)
	{
		if (string.IsNullOrEmpty(datasetPrefix) || entries == null || entries.Count == 0)
		{
			return;
		}
		int count = entries.Count;
		Table table = new Table();
		table.AddColumn("Time (Ticks)", typeof(long));
		table.AddColumn("Severity", typeof(int));
		table.AddColumn("Code", typeof(string));
		table.AddColumn("Data Length", typeof(int));
		int count2 = 0;
		for (int i = 0; i < count; i++)
		{
			count2 += entries[i].Data?.Length ?? 0;
		}
		string[] strings = new string[count2];
		int ordinal = 0;
		for (int j = 0; j < count; j++)
		{
			MessageLogEntry messageLogEntry = entries[j];
			string[] data = messageLogEntry.Data;
			int count3 = data?.Length ?? 0;
			table.AddNewRow(new object[4]
			{
				messageLogEntry.Timestamp.Ticks,
				(int)messageLogEntry.Severity,
				messageLogEntry.Code ?? "",
				count3
			});
			for (int k = 0; k < count3; k++)
			{
				strings[ordinal++] = data[k] ?? "";
			}
		}
		WriteDataset(datasetPrefix + " (Data)", strings);
		WriteCompoundTable(datasetPrefix + " (Structure)", table);
	}

	public void WriteDataset<T>(string datasetName, T[,] values) where T : unmanaged
	{
		long timestamp = Stopwatch.GetTimestamp();
		WriteDatasetPreCheck(datasetName);
		ulong[] size = new ulong[2]
		{
			(ulong)values.GetLength(0),
			(ulong)values.GetLength(1)
		};
		ulong[] chunking = OptimalChunking(values);
		using (H5DatasetID dataset = H5DatasetID.CreateBasic(FileId, datasetName, typeof(T), size, chunking))
		{
			if (values.Length == 0)
			{
				return;
			}
			H5DWriteInternal(dataset, values);
		}
		LogDatasetWrite(datasetName, values.Length, timestamp);
	}

	public void WriteDataset(string datasetName, string[,] values)
	{
		WriteDatasetPreCheck(datasetName);
		ulong[] size = new ulong[2]
		{
			(ulong)values.GetLength(0),
			(ulong)values.GetLength(1)
		};
		ulong[] chunking = OptimalChunking(values);
		int maxStringSize = GetMaxStringSize(values);
		using H5DatasetID dataset = H5DatasetID.CreateBasic(FileId, datasetName, typeof(string), size, chunking, maxStringSize);
		if (values.Length != 0)
		{
			H5DWriteInternal(dataset, SerializeAligned(values, maxStringSize));
		}
	}

	public unsafe void WriteDatasetUnsafe<T>(string datasetName, T* values, int dim0Size) where T : unmanaged
	{
		WriteDatasetPreCheck(datasetName);
		ulong[] size = new ulong[1] { (ulong)dim0Size };
		ulong[] chunking = OptimalChunking(dim0Size, sizeof(T));
		using H5DatasetID h5DatasetID = H5DatasetID.CreateBasic(FileId, datasetName, typeof(T), size, chunking);
		H5D.write(h5DatasetID.DatasetId, h5DatasetID.DataType.TypeId, 0L, 0L, 0L, new IntPtr(values));
	}

	public unsafe void WriteDatasetUnsafe<T>(string datasetName, T* values, int dim0Size, int dim1Size) where T : unmanaged
	{
		WriteDatasetPreCheck(datasetName);
		ulong[] size = new ulong[2]
		{
			(ulong)dim0Size,
			(ulong)dim1Size
		};
		ulong[] chunking = OptimalChunking(dim0Size, dim1Size, sizeof(T));
		using H5DatasetID h5DatasetID = H5DatasetID.CreateBasic(FileId, datasetName, typeof(T), size, chunking);
		H5D.write(h5DatasetID.DatasetId, h5DatasetID.DataType.TypeId, 0L, 0L, 0L, new IntPtr(values));
	}

	public void WriteUncompressed<T>(string datasetName, T[] values) where T : unmanaged
	{
		WriteDatasetPreCheck(datasetName);
		ulong[] size = new ulong[1] { (ulong)values.Length };
		using H5DatasetID dataset = H5DatasetID.CreateBasicUncompressed(FileId, datasetName, typeof(T), size);
		H5DWriteInternal(dataset, values);
	}

	public void Create2dExtendibleDataset<T>(string datasetName, int numRowsPerChunk, int numColsPerChunk, T fillValue = default(T)) where T : unmanaged
	{
		WriteDatasetPreCheck(datasetName);
		using (H5DatasetID.CreateExtendible(chunking: new ulong[2]
		{
			(ulong)numRowsPerChunk,
			(ulong)numColsPerChunk
		}, fileId: FileId, datasetName: datasetName, fillValue: fillValue))
		{
		}
	}

	public H5DatasetID CreateAndOpen2dExtendibleDataset<T>(string datasetName, int numRowsPerChunk, int numColsPerChunk) where T : unmanaged
	{
		WriteDatasetPreCheck(datasetName);
		return H5DatasetID.CreateExtendible(chunking: new ulong[2]
		{
			(ulong)numRowsPerChunk,
			(ulong)numColsPerChunk
		}, fileId: FileId, datasetName: datasetName, type: typeof(T));
	}

	public void Create1dExtendibleDataset<T>(string datasetName, int numElementsPerChunk) where T : unmanaged
	{
		WriteDatasetPreCheck(datasetName);
		using (H5DatasetID.CreateExtendible(chunking: new ulong[1] { (ulong)numElementsPerChunk }, fileId: FileId, datasetName: datasetName, type: typeof(T)))
		{
		}
	}

	public H5DatasetID CreateAndOpen1dExtendibleDataset<T>(string datasetName, int numElementsPerChunk) where T : unmanaged
	{
		WriteDatasetPreCheck(datasetName);
		return H5DatasetID.CreateExtendible(chunking: new ulong[1] { (ulong)numElementsPerChunk }, fileId: FileId, datasetName: datasetName, type: typeof(T));
	}

	public void Create3dExtendibleDataset(string datasetName, Type type, int numRowsPerChunk, int numColsPerChunk, int numPlanesPerChunk)
	{
		WriteDatasetPreCheck(datasetName);
		if (type == typeof(string))
		{
			throw new NotImplementedException();
		}
		ulong[] ulongs = new ulong[3];
		ulongs[0] = (ulong)numRowsPerChunk;
		ulongs[1] = (ulong)numColsPerChunk;
		ulongs[1] = (ulong)numPlanesPerChunk;
		using (H5DatasetID.CreateExtendible(FileId, datasetName, type, ulongs))
		{
		}
	}

	private unsafe static void RegisterEnumOnce()
	{
		if (!_hasRegistered)
		{
			_hasRegistered = true;
			_fileAccessType = H5T.enum_create(H5T.NATIVE_INT);
			int ordinal = 0;
			int unity = 1;
			int ordinal2 = 2;
			_fileAccessRead = H5T.enum_insert(_fileAccessType, "Read", (nint)(&ordinal));
			_fileAccessWrite = H5T.enum_insert(_fileAccessType, "Write", (nint)(&unity));
			_fileAccessWrite = H5T.enum_insert(_fileAccessType, "Both", (nint)(&ordinal2));
			H5T.get_nmembers(_fileAccessType);
			int unity2 = -1;
			H5T.get_member_value(_fileAccessType, 0u, (nint)(&unity2));
			StringBuilder name = new StringBuilder(16);
			H5T.enum_nameof(_fileAccessType, unity2, name, 16);
		}
	}

	public unsafe void CreateTestEnum()
	{
		RegisterEnumOnce();
		string path = "/group";
		string attr_name = "attr";
		CreateGroup(path);
		using H5ObjectID h5ObjectID = new H5ObjectID(FileId, path);
		long fileAccessType = _fileAccessType;
		long space_id = H5S.create(H5S.class_t.SCALAR);
		long attr_id = H5A.create(h5ObjectID.ObjectId, attr_name, fileAccessType, space_id, 0L, 0L);
		int ordinal = 3;
		H5A.write(attr_id, fileAccessType, (nint)(&ordinal));
		H5A.close(attr_id);
		H5S.close(space_id);
	}

	public void WriteTable_Old(string datasetName, Table table)
	{
		CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
		for (int i = 0; i < table.Columns.Count; i++)
		{
			TableColumn tableColumn = table.Columns[i];
			string name = tableColumn.Name;
			Type type = tableColumn.Type;
			if (type.IsPrimitive)
			{
				compoundTypeDefinition.AddField(type, name);
				continue;
			}
			if (type == typeof(string))
			{
				int table2Term = 0;
				for (int j = 0; j < table.Rows.Count; j++)
				{
					string table2 = table[j][i] as string;
					if (!string.IsNullOrEmpty(table2))
					{
						table2Term = Math.Max(table2Term, Encoding.UTF8.GetByteCount(table2));
					}
				}
				compoundTypeDefinition.AddStringField(name, table2Term);
				continue;
			}
			if (type.IsEnum)
			{
				throw new NotSupportedException();
			}
			throw new NotSupportedException("Column type must be a primitive or string. Actual: " + type.FullName.ToString());
		}
		Create1DCompoundDataset(datasetName, 100, compoundTypeDefinition);
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ulong[] hyperslabDims = new ulong[1] { 1uL };
		for (int k = 0; k < table.Rows.Count; k++)
		{
			object[] itemArray = table.Rows[k].ItemArray;
			using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendElements(h5DatasetID, hyperslabDims);
			H5DWriteInternal(h5DatasetID, h5HyperslabID.SpaceId, ByteifyObjects(itemArray, h5DatasetID.DataType));
		}
	}

	public unsafe void WriteCompoundTable(string datasetName, Table table, CompoundTableOptions opts = null)
	{
		if (opts == null)
		{
			opts = CompoundTableOptions.Default();
		}
		Delete(datasetName);
		int[] intValues = new int[table.Columns.Count];
		int ordinal = 0;
		for (int i = 0; i < table.Columns.Count; i++)
		{
			TableColumn tableColumn = table.Columns[i];
			if (tableColumn.Type == typeof(string))
			{
				int table2Term = 1;
				for (int j = 0; j < table.Rows.Count; j++)
				{
					string table2 = table[j][i] as string;
					if (!string.IsNullOrEmpty(table2))
					{
						table2Term = Math.Max(table2Term, Encoding.UTF8.GetByteCount(table2));
					}
				}
				intValues[i] = table2Term;
			}
			else if (tableColumn.Type.IsEnum && opts.EncodeEnumsAsStrings)
			{
				Dictionary<object, string> serializeMap = AliasAttribute.GetSerializeMap(tableColumn.Type);
				int ordinal2 = 1;
				for (int k = 0; k < table.Rows.Count; k++)
				{
					object table3 = table[k][i];
					if (table3 != null && serializeMap.TryGetValue(table3, out var localValue) && !string.IsNullOrEmpty(localValue))
					{
						ordinal2 = Math.Max(ordinal2, Encoding.UTF8.GetByteCount(localValue));
					}
				}
				intValues[i] = ordinal2;
			}
			else
			{
				intValues[i] = CompoundTypeDefinition.GetTypeSize(tableColumn.Type);
			}
			ordinal += intValues[i];
		}
		long create = H5T.create(H5T.class_t.COMPOUND, ordinal);
		Action<object, Memory<byte>>[] actionArray = new Action<object, Memory<byte>>[table.Columns.Count];
		bool[] reportedBadValue = new bool[table.Columns.Count];
		int ordinal3 = 0;
		for (int l = 0; l < table.Columns.Count; l++)
		{
			int offsetTmp = ordinal3;
			int colIdxTmp = l;
			TableColumn tableColumn2 = table.Columns[l];
			if (tableColumn2.Type == typeof(string))
			{
				actionArray[l] = (object localValue2, Memory<byte> buf) =>
				{
					H5StringEncoding.WriteFixed(localValue2 as string, buf.Span.Slice(offsetTmp, intValues[colIdxTmp]));
				};
				long h5TC_S1Term = H5T.copy(H5T.C_S1);
				int ordinal4 = intValues[l];
				H5T.set_size(h5TC_S1Term, ordinal4);
				H5T.set_cset(h5TC_S1Term, H5T.cset_t.UTF8);
				H5T.insert(create, tableColumn2.Name, ordinal3, h5TC_S1Term);
				ordinal3 += ordinal4;
				H5T.close(h5TC_S1Term);
				continue;
			}
			if (tableColumn2.Type.IsEnum && opts.EncodeEnumsAsStrings)
			{
				Dictionary<object, string> serializer = AliasAttribute.GetSerializeMap(tableColumn2.Type);
				actionArray[l] = (object localValue3, Memory<byte> buf) =>
				{
					string textValue;
					if (localValue3 == null)
					{
						textValue = "";
					}
					else if (!serializer.TryGetValue(localValue3, out textValue))
					{
						textValue = "";
					}
					H5StringEncoding.WriteFixed(textValue, buf.Span.Slice(offsetTmp, intValues[colIdxTmp]));
				};
				long h5TC_S1Term2 = H5T.copy(H5T.C_S1);
				int ordinal5 = intValues[l];
				H5T.set_size(h5TC_S1Term2, ordinal5);
				H5T.set_cset(h5TC_S1Term2, H5T.cset_t.UTF8);
				H5T.insert(create, tableColumn2.Name, ordinal3, h5TC_S1Term2);
				ordinal3 += ordinal5;
				H5T.close(h5TC_S1Term2);
				continue;
			}
			if (tableColumn2.Type == typeof(byte))
			{
				actionArray[l] = (object localValue4, Memory<byte> buf) =>
				{
					if (!ParseHelpers.TryUnbox<byte>(localValue4, 0, out var localValue5))
					{
						ReportBadValue(colIdxTmp, localValue4);
					}
					buf.Span[offsetTmp] = localValue5;
				};
			}
			else if (tableColumn2.Type == typeof(bool))
			{
				actionArray[l] = (object localValue6, Memory<byte> buf) =>
				{
					if (!ParseHelpers.TryUnbox(localValue6, fallback: false, out var localValue7))
					{
						ReportBadValue(colIdxTmp, localValue6);
					}
					BitConverter.TryWriteBytes(buf.Span.Slice(offsetTmp), localValue7);
				};
			}
			else if (tableColumn2.Type == typeof(short))
			{
				actionArray[l] = (object localValue8, Memory<byte> buf) =>
				{
					if (!ParseHelpers.TryUnbox(localValue8, short.MinValue, out var localValue9))
					{
						ReportBadValue(colIdxTmp, localValue8);
					}
					BitConverter.TryWriteBytes(buf.Span.Slice(offsetTmp), localValue9);
				};
			}
			else if (tableColumn2.Type == typeof(int))
			{
				actionArray[l] = (object localValue10, Memory<byte> buf) =>
				{
					if (!ParseHelpers.TryUnbox(localValue10, int.MinValue, out var localValue11))
					{
						ReportBadValue(colIdxTmp, localValue10);
					}
					BitConverter.TryWriteBytes(buf.Span.Slice(offsetTmp), localValue11);
				};
			}
			else if (tableColumn2.Type == typeof(long) || tableColumn2.Type == typeof(long?))
			{
				actionArray[l] = (object localValue12, Memory<byte> buf) =>
				{
					if (!ParseHelpers.TryUnbox(localValue12, long.MinValue, out var localValue13))
					{
						ReportBadValue(colIdxTmp, localValue12);
					}
					BitConverter.TryWriteBytes(buf.Span.Slice(offsetTmp), localValue13);
				};
			}
			else if (tableColumn2.Type == typeof(float) || tableColumn2.Type == typeof(float?))
			{
				actionArray[l] = (object localValue14, Memory<byte> buf) =>
				{
					if (!ParseHelpers.TryUnbox(localValue14, float.NaN, out var localValue15))
					{
						ReportBadValue(colIdxTmp, localValue14);
					}
					BitConverter.TryWriteBytes(buf.Span.Slice(offsetTmp), localValue15);
				};
			}
			else
			{
				if (!(tableColumn2.Type == typeof(double)) && !(tableColumn2.Type == typeof(double?)))
				{
					if (tableColumn2.Type.IsEnum)
					{
						if (CompoundTypeDefinition.GetTypeSize(tableColumn2.Type.GetEnumUnderlyingType()) != 4)
						{
							throw new NotSupportedException("Only 4-byte wide enums supported currently.");
						}
						actionArray[l] = (object minValue2, Memory<byte> buf) =>
						{
							int minValue = int.MinValue;
							if (minValue2 != null)
							{
								try
								{
									minValue = (int)minValue2;
								}
								catch
								{
									minValue = int.MinValue;
									ReportBadValue(colIdxTmp, minValue2);
								}
							}
							BitConverter.TryWriteBytes(buf.Span.Slice(offsetTmp), minValue);
						};
						H5T.insert(create, tableColumn2.Name, ordinal3, H5DataTypeID.BuiltinTypeMap[typeof(int)]);
						ordinal3 += intValues[l];
						continue;
					}
					throw new NotSupportedException();
				}
				actionArray[l] = (object localValue16, Memory<byte> buf) =>
				{
					if (!ParseHelpers.TryUnbox(localValue16, double.NaN, out var localValue17))
					{
						ReportBadValue(colIdxTmp, localValue16);
					}
					BitConverter.TryWriteBytes(buf.Span.Slice(offsetTmp), localValue17);
				};
			}
			H5T.insert(create, tableColumn2.Name, ordinal3, H5DataTypeID.BuiltinTypeMap[tableColumn2.Type]);
			ordinal3 += intValues[l];
		}
		ulong rowCount = (ulong)table.Rows.Count;
		ulong ordinal6 = 100uL;
		ulong maxValue = ulong.MaxValue;
		long create_simple = H5S.create_simple(1, &rowCount, &maxValue);
		long create2 = H5P.create(H5P.DATASET_CREATE);
		H5P.set_chunk(create2, 1, &ordinal6);
		if (opts.CompressDataset)
		{
			H5P.set_deflate(create2, 1u);
		}
		long dset_id = H5D.create(FileId, datasetName, create, create_simple, 0L, create2, 0L);
		byte[] byteValues = new byte[table.Rows.Count * ordinal];
		for (int intervalIndex = 0; intervalIndex < table.Rows.Count; intervalIndex++)
		{
			object[] itemArray = table.Rows[intervalIndex].ItemArray;
			Memory<byte> slice = byteValues.AsMemory().Slice(intervalIndex * ordinal, ordinal);
			for (int intervalIndex2 = 0; intervalIndex2 < itemArray.Length; intervalIndex2++)
			{
				TableColumn tableColumn3 = table.Columns[intervalIndex2];
				object itemArray2 = itemArray[intervalIndex2];
				if (itemArray2 == null && tableColumn3.Type == typeof(string))
				{
					itemArray2 = string.Empty;
				}
				actionArray[intervalIndex2](itemArray2, slice);
			}
		}
		using (MemoryHandle memoryHandle = byteValues.AsMemory().Pin())
		{
			int write = H5D.write(dset_id, create, create_simple, 0L, 0L, (nint)memoryHandle.Pointer);
			int close = H5T.close(create);
			int close2 = H5S.close(create_simple);
			int close3 = H5P.close(create2);
			int close4 = H5D.close(dset_id);
			if (write < 0 || close < 0 || close2 < 0 || close3 < 0 || close4 < 0)
			{
				_logger.LogWarning("Error writing compound table to h5.");
			}
		}
		void ReportBadValue(int badColIdx, object localValue18)
		{
			if (!reportedBadValue[badColIdx])
			{
				reportedBadValue[badColIdx] = true;
				TableColumn tableColumn4 = table.Columns[badColIdx];
				_logger.LogWarning("Column \"{Column}\" of \"{Dataset}\" holds {ValueType} values but is typed {ColumnType}. Writing the fallback value - data will be lost.", tableColumn4.Name, datasetName, localValue18?.GetType().Name ?? "null", tableColumn4.Type.Name);
			}
		}
	}

	public void Create1DCompoundDataset(string datasetName, int numElementsPerChunk, CompoundTypeDefinition compoundType)
	{
		WriteDatasetPreCheck(datasetName);
		using (H5DatasetID.CreateCompound(chunking: new ulong[1] { (ulong)numElementsPerChunk }, fileId: FileId, datasetName: datasetName, compoundType: compoundType))
		{
		}
	}

	public void AddCompoundElement(string datasetName, object[] row)
	{
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendElements(h5DatasetID, new ulong[1] { 1uL });
		H5DWriteInternal(h5DatasetID, h5HyperslabID.SpaceId, ByteifyObjects(row, h5DatasetID.DataType));
	}

	public void AddBuffer1D(string datasetName, byte[] buffer)
	{
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		CompoundTypeDefinition compoundDataType = h5DatasetID.DataType.GetCompoundDataType();
		int sizeSum = 0;
		foreach (CompoundFieldDefinition field in compoundDataType.Fields)
		{
			sizeSum += field.Size;
		}
		if (buffer.Length % sizeSum != 0)
		{
			throw new ArgumentException("Buffer size not an integer number of elements");
		}
		int lengthOverSizeSum = buffer.Length / sizeSum;
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendElements(h5DatasetID, new ulong[1] { (ulong)lengthOverSizeSum });
		H5DWriteInternal(h5DatasetID, h5HyperslabID.SpaceId, buffer);
	}

	private static byte[] ByteifyObjects(object[] objs, H5DataTypeID datatype)
	{
		CompoundTypeDefinition compoundDataType = datatype.GetCompoundDataType();
		if (objs.Length != compoundDataType.Fields.Count)
		{
			throw new ArgumentException("Incorrect compound type definition");
		}
		byte[] byteValues = new byte[datatype.GetSize()];
		byte[] bytes = null;
		int byteLengthSum = 0;
		for (int i = 0; i < objs.Length; i++)
		{
			CompoundFieldDefinition compoundFieldDefinition = compoundDataType.Fields[i];
			object localValue = objs[i];
			if (localValue == null && compoundFieldDefinition.Type == typeof(string))
			{
				localValue = string.Empty;
			}
			Type type = localValue.GetType();
			if (type != compoundFieldDefinition.Type)
			{
				throw new ArgumentException("Incorrect compound type definition");
			}
			if (type == typeof(byte))
			{
				bytes = new byte[1] { (byte)localValue };
			}
			else if (type == typeof(bool))
			{
				bytes = BitConverter.GetBytes((bool)localValue);
			}
			else if (type == typeof(short))
			{
				bytes = BitConverter.GetBytes((short)localValue);
			}
			else if (type == typeof(int))
			{
				bytes = BitConverter.GetBytes((int)localValue);
			}
			else if (type == typeof(long))
			{
				bytes = BitConverter.GetBytes((long)localValue);
			}
			else if (type == typeof(float))
			{
				bytes = BitConverter.GetBytes((float)localValue);
			}
			else if (type == typeof(double))
			{
				bytes = BitConverter.GetBytes((double)localValue);
			}
			else if (type == typeof(string))
			{
				int size = compoundFieldDefinition.Size;
				string textValue = (string)localValue;
				bytes = new byte[size];
				H5StringEncoding.WriteFixed(textValue, bytes);
			}
			Buffer.BlockCopy(bytes, 0, byteValues, byteLengthSum, bytes.Length);
			byteLengthSum += bytes.Length;
		}
		return byteValues;
	}

	public void AddElements<T>(string datasetName, T[] values, int elementsToWrite = int.MinValue)
	{
		using H5DatasetID dsetId = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		AddElements(dsetId, values, elementsToWrite);
	}

	public void AddElements<T>(H5DatasetID dsetId, T[] values, int elementsToWrite = int.MinValue)
	{
		ulong[] ulongs = new ulong[1];
		if (elementsToWrite == int.MinValue)
		{
			ulongs[0] = (ulong)values.Length;
		}
		else
		{
			ulongs[0] = (ulong)elementsToWrite;
		}
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendElements(dsetId, ulongs);
		H5DWriteInternal(dsetId, h5HyperslabID.SpaceId, values);
	}

	public void InsertChunk<T>(string datasetName, T[] values, int chunkIdx) where T : unmanaged
	{
		long getChunkSizeDatasetNameTerm = GetChunkSize(datasetName)[0] * chunkIdx;
		InsertValues(datasetName, values, (int)getChunkSizeDatasetNameTerm);
	}

	public void InsertChunk<T>(string datasetName, T[,] values, int chunkRowIdx, int chunkColIdx) where T : unmanaged
	{
		long[] chunkSize = GetChunkSize(datasetName);
		long chunkSizeChunkRowIdxTerm = chunkSize[0] * chunkRowIdx;
		long chunkSizeChunkColIdxTerm = chunkSize[1] * chunkColIdx;
		InsertValues(datasetName, values, (int)chunkSizeChunkRowIdxTerm, (int)chunkSizeChunkColIdxTerm);
	}

	public void InsertValues<T>(string datasetName, T[] values, int startIdx) where T : unmanaged
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Insert1D(dataset, new ulong[1] { (ulong)values.Length }, startIdx);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void InsertValues<T>(string datasetName, T[,] values, int startRow, int startCol) where T : unmanaged
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Insert2D(dataset, new ulong[2]
		{
			(ulong)values.GetLength(0),
			(ulong)values.GetLength(1)
		}, startRow, startCol);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddColumns<T>(string datasetName, T[,] values)
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		AddColumns(dataset, values);
	}

	public void AddColumns<T>(H5DatasetID dataset, T[,] values)
	{
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendColumns(dataset, new ulong[2]
		{
			(ulong)values.GetLength(0),
			(ulong)values.GetLength(1)
		});
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddColumn<T>(string datasetName, T[] values)
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		AddColumn(dataset, values);
	}

	public void AddColumn<T>(H5DatasetID dataset, T[] values)
	{
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendColumns(dataset, new ulong[2]
		{
			(ulong)values.Length,
			1uL
		});
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddRow<T>(string datasetName, T[] values)
	{
		using H5DatasetID dataset = OpenDataset(datasetName);
		AddRow(dataset, values);
	}

	public void AddRow<T>(H5DatasetID dataset, T[] values)
	{
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendRows(dataset, new ulong[2]
		{
			1uL,
			(ulong)values.Length
		});
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddRow<T>(string datasetName, Memory<T> values) where T : unmanaged
	{
		using H5DatasetID dataset = OpenDataset(datasetName);
		AddRow(dataset, values);
	}

	public void AddRow<T>(H5DatasetID dataset, Memory<T> values) where T : unmanaged
	{
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendRows(dataset, new ulong[2]
		{
			1uL,
			(ulong)values.Length
		});
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddRow<T>(string datasetName, Memory<T> values, int rowIndex) where T : unmanaged
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.InsertRows(dataset, new ulong[2]
		{
			1uL,
			(ulong)values.Length
		}, rowIndex);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddRows<T>(string datasetName, T[,] values, int rowsToWrite = int.MinValue) where T : unmanaged
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ulong[] ulongs = new ulong[2];
		if (rowsToWrite == int.MinValue)
		{
			ulongs[0] = (ulong)values.GetLength(0);
		}
		else
		{
			ulongs[0] = (ulong)rowsToWrite;
		}
		ulongs[1] = (ulong)values.GetLength(1);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendRows(dataset, ulongs);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddRows<T>(string datasetName, T[,] values, int rowsToWrite, int dstStartRow) where T : unmanaged
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ulong[] ulongs = new ulong[2];
		if (rowsToWrite == int.MinValue)
		{
			ulongs[0] = (ulong)values.GetLength(0);
		}
		else
		{
			ulongs[0] = (ulong)rowsToWrite;
		}
		ulongs[1] = (ulong)values.GetLength(1);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.InsertRows(dataset, ulongs, dstStartRow);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddRow<T>(string datasetName, T[] values, int rowIndex)
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.InsertRows(dataset, new ulong[2]
		{
			1uL,
			(ulong)values.Length
		}, rowIndex);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void WriteRow<T>(string datasetName, int rowIdx, T[] values) where T : unmanaged
	{
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		WriteRow(dataset, rowIdx, values);
	}

	public void WriteRow<T>(H5DatasetID dataset, int rowIdx, T[] values) where T : unmanaged
	{
		ulong[] dimensions = GetDimensions(dataset.SpaceId);
		RowColArgCheck(rowIdx, 1, 0, (int)dimensions[1], dimensions, dataset.DatasetName, "WriteRow");
		Hyperslab.GetRowDims(rowIdx, 1, dimensions, out var hyperslabDims, out var offset);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataset.SpaceId, hyperslabDims, offset);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void WriteRow<T>(H5DatasetID dataset, int rowIdx, Memory<T> values) where T : unmanaged
	{
		ulong[] dimensions = GetDimensions(dataset.SpaceId);
		RowColArgCheck(rowIdx, 1, 0, (int)dimensions[1], dimensions, dataset.DatasetName, "WriteRow");
		Hyperslab.GetRowDims(rowIdx, 1, dimensions, out var hyperslabDims, out var offset);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Open(dataset.SpaceId, hyperslabDims, offset);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void Rechunk2D<T>(string datasetName, int newChunkRowCount) where T : unmanaged
	{
		string datasetNameTerm = datasetName + "__TEMP__";
		if (ExistsAsDataset(datasetNameTerm))
		{
			Delete(datasetNameTerm);
		}
		using (H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true))
		{
			h5DatasetID.GetNativeIDs(out var _, out var _, out var dataspaceID);
			ulong[] dimensions = GetDimensions(dataspaceID);
			int dimension = (int)dimensions[0];
			int dimension2 = (int)dimensions[1];
			using H5DatasetID dataset = H5DatasetID.CreateExtendible(chunking: new ulong[2]
			{
				(ulong)newChunkRowCount,
				(ulong)dimension2
			}, fileId: FileId, datasetName: datasetNameTerm, type: typeof(T));
			int count = (int)Math.Ceiling((double)dimensions[0] / (double)newChunkRowCount);
			T[,] values = null;
			for (int i = 0; i < count; i++)
			{
				int newChunkRowCountTerm = i * newChunkRowCount;
				int numRows = Math.Min(newChunkRowCount, dimension - newChunkRowCountTerm);
				ReadRows(h5DatasetID, newChunkRowCountTerm, numRows, ref values);
				AddRows(dataset, values);
			}
		}
		Delete(datasetName);
		CopyTo(this, datasetNameTerm, datasetName);
		Delete(datasetNameTerm);
	}

	public void AddRows<T>(H5DatasetID dataset, T[,] values, int rowsToWrite, int dstStartRow) where T : unmanaged
	{
		ulong[] ulongs = new ulong[2];
		if (rowsToWrite == int.MinValue)
		{
			ulongs[0] = (ulong)values.GetLength(0);
		}
		else
		{
			ulongs[0] = (ulong)rowsToWrite;
		}
		ulongs[1] = (ulong)values.GetLength(1);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.InsertRows(dataset, ulongs, dstStartRow);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddRows<T>(H5DatasetID dataset, T[,] values, int rowsToWrite = int.MinValue) where T : unmanaged
	{
		ulong[] ulongs = new ulong[2];
		if (rowsToWrite == int.MinValue)
		{
			ulongs[0] = (ulong)values.GetLength(0);
		}
		else
		{
			ulongs[0] = (ulong)rowsToWrite;
		}
		ulongs[1] = (ulong)values.GetLength(1);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendRows(dataset, ulongs);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddStringRows(string datasetName, string[,] values, int rowsToWrite = int.MinValue)
	{
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ulong[] ulongs = new ulong[1];
		if (rowsToWrite == int.MinValue)
		{
			ulongs[0] = (ulong)values.Length;
		}
		else
		{
			ulongs[0] = (ulong)rowsToWrite;
		}
		ulongs[1] = (ulong)values.GetLength(1);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendRows(h5DatasetID, ulongs);
		int size = h5DatasetID.DataType.GetSize();
		byte[,] buffer = SerializeAligned(values, size);
		H5DWriteInternal(h5DatasetID, h5HyperslabID.SpaceId, buffer);
	}

	public void WriteJagged<T, K>(string dataset, IList<T> elements, int valsPerPoint, Func<T, int> getPointCount, Action<T, int, K[]> getPointData) where K : unmanaged
	{
		int maxBytesPerWrite = 131072;
		if (ExistsAsDataset(dataset))
		{
			Delete(dataset);
		}
		if (elements.Count == 0)
		{
			return;
		}
		int sum = elements.Sum((T pl) => (pl != null) ? getPointCount(pl) : 0);
		if (sum == 0)
		{
			return;
		}
		int bytesPerValue = Marshal.SizeOf<K>();
		int numRowsPerChunk = H5Reader.RowChunkSize(sum, valsPerPoint, maxBytesPerWrite, bytesPerValue);
		Create2dExtendibleDataset<K>(dataset, numRowsPerChunk, valsPerPoint);
		int[] intValues = new int[elements.Count];
		int[] intValuesArray = new int[elements.Count];
		using (H5DatasetID dataset2 = OpenDataset(dataset))
		{
			int getPointCountSum = 0;
			K[] keys = new K[valsPerPoint];
			for (int intervalIndex = 0; intervalIndex < elements.Count; intervalIndex++)
			{
				T element = elements[intervalIndex];
				int getPointCount2 = ((element != null) ? getPointCount(element) : 0);
				intValues[intervalIndex] = getPointCountSum;
				intValuesArray[intervalIndex] = getPointCount2;
				getPointCountSum += getPointCount2;
				if (getPointCount2 <= 0)
				{
					continue;
				}
				K[,] keys2 = new K[getPointCount2, valsPerPoint];
				for (int intervalIndex2 = 0; intervalIndex2 < getPointCount2; intervalIndex2++)
				{
					getPointData(element, intervalIndex2, keys);
					for (int intervalIndex3 = 0; intervalIndex3 < valsPerPoint; intervalIndex3++)
					{
						keys2[intervalIndex2, intervalIndex3] = keys[intervalIndex3];
					}
				}
				AddRows(dataset2, keys2);
			}
		}
		WriteAttribute(dataset, "Start", intValues);
		WriteAttribute(dataset, "Count", intValuesArray);
	}

	public void WriteJagged_Old<T, K>(string infoDataset, string valueDataset, IList<T> elements, int valsPerPoint, Func<T, int> getPointCount, Action<T, int, K[]> getPointData) where K : unmanaged
	{
		int maxBytesPerWrite = 131072;
		if (ExistsAsDataset(infoDataset))
		{
			Delete(infoDataset);
		}
		if (ExistsAsDataset(valueDataset))
		{
			Delete(valueDataset);
		}
		if (elements.Count == 0)
		{
			return;
		}
		int sum = elements.Sum((T pl) => (pl != null) ? getPointCount(pl) : 0);
		if (sum == 0)
		{
			return;
		}
		int bytesPerValue = Marshal.SizeOf<K>();
		Create2dExtendibleDataset(infoDataset, Math.Min(elements.Count, 256), 2, 0);
		int numRowsPerChunk = H5Reader.RowChunkSize(sum, valsPerPoint, maxBytesPerWrite, bytesPerValue);
		Create2dExtendibleDataset<K>(valueDataset, numRowsPerChunk, valsPerPoint);
		using H5DatasetID dataset = OpenDataset(infoDataset);
		using H5DatasetID dataset2 = OpenDataset(valueDataset);
		int getPointCountSum = 0;
		int[] intValues = new int[2];
		K[] keys = new K[valsPerPoint];
		for (int intervalIndex = 0; intervalIndex < elements.Count; intervalIndex++)
		{
			T element = elements[intervalIndex];
			int getPointCount2 = ((element != null) ? getPointCount(element) : 0);
			intValues[0] = getPointCountSum;
			intValues[1] = getPointCount2;
			AddRow(dataset, intValues);
			getPointCountSum += getPointCount2;
			if (getPointCount2 <= 0)
			{
				continue;
			}
			K[,] keys2 = new K[getPointCount2, valsPerPoint];
			for (int intervalIndex2 = 0; intervalIndex2 < getPointCount2; intervalIndex2++)
			{
				getPointData(element, intervalIndex2, keys);
				for (int intervalIndex3 = 0; intervalIndex3 < valsPerPoint; intervalIndex3++)
				{
					keys2[intervalIndex2, intervalIndex3] = keys[intervalIndex3];
				}
			}
			AddRows(dataset2, keys2);
		}
	}

	public unsafe void WriteVariableLenStringArray(string datasetName, string[] values)
	{
		WriteDatasetPreCheck(datasetName);
		ulong[] size = new ulong[1] { (ulong)values.Length };
		using H5DatasetID dataset = H5DatasetID.CreateBasicUncompressed(FileId, datasetName, typeof(string), size, (int)H5T.VARIABLE);
		nint[] nints = new nint[values.Length];
		byte[] serializePacked = SerializePacked(values);
		fixed (byte* serializePacked2 = serializePacked)
		{
			nint serializePacked3 = new IntPtr(serializePacked2);
			int lengthOffsetSum = 0;
			for (int i = 0; i < values.Length; i++)
			{
				nints[i] = serializePacked3 + lengthOffsetSum;
				string textValue = values[i];
				if (textValue == null)
				{
					textValue = string.Empty;
				}
				lengthOffsetSum += Encoding.UTF8.GetByteCount(textValue) + 1;
			}
			H5DWriteInternal(dataset, nints);
		}
	}

	public unsafe void WriteVariableLenArray<T>(string datasetName, T[][] values) where T : unmanaged
	{
		WriteDatasetPreCheck(datasetName);
		ulong[] size = new ulong[1] { (ulong)values.Length };
		using H5DatasetID dataset = H5DatasetID.CreateUncompressedVlen<T>(FileId, datasetName, size);
		VLenType[] vLenTypes = new VLenType[values.Length];
		List<GCHandle> gcHandles = new List<GCHandle>();
		try
		{
			for (int i = 0; i < values.Length; i++)
			{
				gcHandles.Add(GCHandle.Alloc(values[i], GCHandleType.Pinned));
			}
			fixed (VLenType* vLenTypes2 = vLenTypes)
			{
				for (int j = 0; j < values.Length; j++)
				{
					vLenTypes[j].Length = values[j].Length;
					vLenTypes[j].Pointer = gcHandles[j].AddrOfPinnedObject();
				}
				H5DWriteInternal(dataset, vLenTypes);
			}
		}
		catch
		{
			throw;
		}
		finally
		{
			for (int k = 0; k < gcHandles.Count; k++)
			{
				gcHandles[k].Free();
			}
		}
	}

	public void Trim<T>(string datasetName, int rows, int columns) where T : unmanaged
	{
		ReadDataset(datasetName, out T[,] values);
		if (rows == -1)
		{
			rows = values.GetLength(0);
		}
		if (columns == -1)
		{
			columns = values.GetLength(1);
		}
		if (values.GetLength(0) < rows || values.GetLength(1) < columns)
		{
			throw new ArgumentException("Source dataset too small.");
		}
		T[,] rows2 = new T[rows, columns];
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < columns; j++)
			{
				rows2[i, j] = values[i, j];
			}
		}
		WriteDataset(datasetName, rows2);
	}

	public void CopyTo(string sourceObject, string destinationObject)
	{
		if (ExistsAsGroup(destinationObject) || ExistsAsDataset(destinationObject))
		{
			Delete(destinationObject);
		}
		H5O.copy(FileId, sourceObject, FileId, destinationObject, 0L, 0L);
	}

	public void Rename(string sourceObject, string destinationObject)
	{
		CopyTo(sourceObject, destinationObject);
		Delete(sourceObject);
	}

	public void CopyAttributesTo(string sourceObject, string destinationObject)
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
				H5Reader.TransferStringAttribute(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(int))
			{
				H5Reader.TransferAttribute<int>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(long))
			{
				H5Reader.TransferAttribute<long>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(float))
			{
				H5Reader.TransferAttribute<float>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(double))
			{
				H5Reader.TransferAttribute<double>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(bool))
			{
				H5Reader.TransferAttribute<bool>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			throw new NotImplementedException();
		}
	}

	public void AddComment(H5DatasetID dataset, string comment)
	{
		throw new NotImplementedException();
	}

	public unsafe void WriteChunkDirect<T>(H5DatasetID dataset, int[] chunkIndex, Memory<T> values) where T : unmanaged
	{
		using MemoryHandle memoryHandle = values.Pin();
		WriteChunkDirect_Internal_Threadsafe(dataset, chunkIndex, (byte*)memoryHandle.Pointer, values.Length * sizeof(T));
	}

	public unsafe void WriteChunkDirect<T>(H5DatasetID dataset, int[] chunkIndex, T[,] values) where T : unmanaged
	{
		using MemoryHandle memoryHandle = values.AsMemory().Pin();
		WriteChunkDirect_Internal_Threadsafe(dataset, chunkIndex, (byte*)memoryHandle.Pointer, values.Length * Marshal.SizeOf<T>());
	}

	private unsafe void WriteChunkDirect_Internal_Threadsafe(H5DatasetID dataset, int[] chunkIndex, byte* buffer, int bufLen)
	{
		uint filter_mask = 0u;
		long[] cachedChunkSize = GetCachedChunkSize(dataset);
		ulong[] ulongs = new ulong[chunkIndex.Length];
		for (int i = 0; i < chunkIndex.Length; i++)
		{
			ulongs[i] = (ulong)(chunkIndex[i] * cachedChunkSize[i]);
		}
		byte[] buf = null;
		int bufLen2 = 0;
		using (UnmanagedMemoryStream dataStream = new UnmanagedMemoryStream(buffer, bufLen))
		{
			CompressTile_Reuse_ZlibHeaderAdlerSpace(dataStream, out buf, out bufLen2);
		}
		buf[0] = 88;
		buf[1] = 133;
		Span<byte> result = buf.AsSpan().Slice(bufLen2 - 4);
		Adler32.ComputeAdler32(new Span<byte>(buffer, bufLen), result);
		long[] datasetDimensions = GetDatasetDimensions(dataset);
		for (int j = 0; j < datasetDimensions.Length; j++)
		{
			long datasetDimension = datasetDimensions[j];
			long chunkIndexCachedChunkSizeTerm = chunkIndex[j] * cachedChunkSize[j];
			_ = chunkIndex[j];
			_ = cachedChunkSize[j];
			if (datasetDimension < chunkIndexCachedChunkSizeTerm)
			{
				throw new Exception("You need to call SetExtent first to pre-size the dataset to the correct dimensions.");
			}
		}
		using MemoryHandle memoryHandle = buf.AsMemory().Pin();
		Advanced.write_chunk(dataset.DatasetId, 0L, filter_mask, ulongs, bufLen2, (nint)memoryHandle.Pointer);
	}

	public void WriteParallelByRow<T>(string dataset, Func<int, T[]> getRow) where T : unmanaged
	{
		using H5DatasetID dataset2 = OpenDataset(dataset);
		WriteParallelByRow(dataset2, getRow);
	}

	public unsafe void WriteParallelByRow<T>(H5DatasetID dataset, Func<int, T[]> getRow, ProgressReporter prog = null) where T : unmanaged
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		long[] chunkSize = GetChunkSize(dataset);
		long[] datasetDimensions = GetDatasetDimensions(dataset);
		int nRows = (int)datasetDimensions[0];
		int datasetDimension = (int)datasetDimensions[1];
		if (nRows == 0 || datasetDimension == 0)
		{
			throw new Exception("You need to call SetExtent first to pre-size the dataset to the correct dimensions.");
		}
		int nRowsPerChunk = (int)chunkSize[0];
		int nColsPerChunk = (int)chunkSize[1];
		if (datasetDimensions.Length != 2)
		{
			throw new NotSupportedException("Dataset must have 2 dimensions.");
		}
		int chunksTall = (int)Math.Ceiling((float)nRows / (float)nRowsPerChunk);
		ThreadLocal<T[]> tlChunkBuffer = new ThreadLocal<T[]>(() => new T[nRowsPerChunk * nColsPerChunk]);
		int sizeOf = Marshal.SizeOf<T>();
		int rowSizeBytes = sizeOf * nColsPerChunk;
		int chunkSizeBytes = sizeOf * nRowsPerChunk * nColsPerChunk;
		int chunksComplete = 0;
		object progLock = new object();
		Parallel.For(0, chunksTall, (int chunkIdx) =>
		{
			int chunkIdxTimesNRowsPerChunk = chunkIdx * nRowsPerChunk;
			int nRowsPerChunkTerm = chunkIdxTimesNRowsPerChunk + nRowsPerChunk;
			int count = Math.Min(nRowsPerChunkTerm, nRows);
			T[] tlChunkBufferTerm = tlChunkBuffer.Value;
			for (int i = chunkIdxTimesNRowsPerChunk; i < count; i++)
			{
				Buffer.BlockCopy(getRow(i), 0, tlChunkBufferTerm, (i - chunkIdxTimesNRowsPerChunk) * rowSizeBytes, rowSizeBytes);
			}
			if (nRowsPerChunkTerm != count)
			{
				for (int j = (count - chunkIdxTimesNRowsPerChunk) * nColsPerChunk; j < tlChunkBufferTerm.Length; j++)
				{
					tlChunkBufferTerm[j] = default;
				}
			}
			int[] chunkIndex = new int[2] { chunkIdx, 0 };
			using (MemoryHandle memoryHandle = tlChunkBufferTerm.AsMemory().Pin())
			{
				WriteChunkDirect_Internal_Threadsafe(dataset, chunkIndex, (byte*)memoryHandle.Pointer, chunkSizeBytes);
			}
			lock (progLock)
			{
				chunksComplete++;
				prog.ReportProgress((int)(100f * (float)chunksComplete / (float)chunksTall));
			}
		});
	}

	public void SetExtent(H5DatasetID dataset, long[] newDims)
	{
		SetExtent(dataset, H5Reader.LongToULong(newDims));
	}

	public void SetExtent(H5DatasetID dataset, ulong[] newDims)
	{
		H5D.set_extent(dataset.DatasetId, newDims);
		H5S.close(dataset.SpaceId);
		dataset.SpaceId = H5D.get_space(dataset.DatasetId);
	}

	public unsafe void SetExtent(H5DatasetID dataset, ulong nElements)
	{
		H5D.set_extent(dataset.DatasetId, &nElements);
		H5S.close(dataset.SpaceId);
		dataset.SpaceId = H5D.get_space(dataset.DatasetId);
	}

	public void SetExtent(H5DatasetID dataset, int nRows, int nCols)
	{
		SetExtent(dataset, (ulong)nRows, (ulong)nCols);
	}

	public unsafe void SetExtent(H5DatasetID dataset, ulong nRows, ulong nCols)
	{
		ulong* localValue = stackalloc ulong[2];
		*localValue = nRows;
		localValue[1] = nCols;
		H5D.set_extent(dataset.DatasetId, localValue);
		H5S.close(dataset.SpaceId);
		dataset.SpaceId = H5D.get_space(dataset.DatasetId);
	}

	private static void CompressTile_Reuse_ZlibHeaderAdlerSpace(Stream dataStream, out byte[] buf, out int bufLen)
	{
		byte[] compressionBuffersH5ReaderTerm = H5Reader._compressionBuffers.Value;
		int startPos = 2;
		using CustomMemoryStream customMemoryStream = new CustomMemoryStream(compressionBuffersH5ReaderTerm, startPos);
		using DeflateStream deflateStream = new DeflateStream(customMemoryStream, CompressionLevel.Fastest, leaveOpen: true);
		dataStream.CopyTo(deflateStream);
		deflateStream.Close();
		customMemoryStream.AddLength(4);
		bufLen = (int)customMemoryStream.Length;
		buf = customMemoryStream.Buffer;
		H5Reader._compressionBuffers.Value = buf;
	}

	public static void TestDedupeToCommonDatabase(string dbFile, List<string> filesToDedupe)
	{
		using (new H5Writer(dbFile))
		{
		}
	}

	public void Flush()
	{
		int flush = H5F.flush(FileId, H5F.scope_t.GLOBAL);
		_logger.LogDebug("{File} flushed at user request. ({Code})", Filename, flush);
	}

	private unsafe int H5DWriteInternal<T>(long datasetId, long typeId, T[] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5D.write(datasetId, typeId, 0L, 0L, 0L, (nint)memoryHandle.Pointer);
	}

	private unsafe int H5DWriteInternal<T>(long datasetId, long typeId, Memory<T> buffer)
	{
		using MemoryHandle memoryHandle = buffer.Pin();
		return H5D.write(datasetId, typeId, 0L, 0L, 0L, (nint)memoryHandle.Pointer);
	}

	private unsafe int H5DWriteInternal<T>(long datasetId, long typeId, T[,] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5D.write(datasetId, typeId, 0L, 0L, 0L, (nint)memoryHandle.Pointer);
	}

	private unsafe int H5DWriteInternal<T>(long datasetId, long typeId, T[,,] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5D.write(datasetId, typeId, 0L, 0L, 0L, (nint)memoryHandle.Pointer);
	}

	private void H5DWriteInternal<T>(H5DatasetID dataset, T[] buffer)
	{
		H5DWriteInternal(dataset.DatasetId, dataset.DataType.TypeId, buffer);
	}

	private void H5DWriteInternal<T>(H5DatasetID dataset, Memory<T> buffer)
	{
		H5DWriteInternal(dataset.DatasetId, dataset.DataType.TypeId, buffer);
	}

	private void H5DWriteInternal<T>(H5DatasetID dataset, T[,] buffer)
	{
		H5DWriteInternal(dataset.DatasetId, dataset.DataType.TypeId, buffer);
	}

	private void H5DWriteInternal<T>(H5DatasetID dataset, T[,,] buffer)
	{
		H5DWriteInternal(dataset.DatasetId, dataset.DataType.TypeId, buffer);
	}

	private unsafe void H5DWriteInternal<T>(H5DatasetID dataset, long hyperslabSpaceId, Memory<T> buffer)
	{
		using MemoryHandle memoryHandle = buffer.Pin();
		H5D.write(dataset.DatasetId, dataset.DataType.TypeId, hyperslabSpaceId, dataset.SpaceId, 0L, (nint)memoryHandle.Pointer);
	}

	private unsafe int H5DWriteInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, T[] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5D.write(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, (nint)memoryHandle.Pointer);
	}

	private unsafe int H5DWriteInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, T[,] buffer)
	{
		using MemoryHandle memoryHandle = buffer.AsMemory().Pin();
		return H5D.write(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, (nint)memoryHandle.Pointer);
	}

	private int H5DWriteInternal<T>(H5DatasetID dataset, long hyperslabSpaceId, T[] buffer)
	{
		return H5DWriteInternal(dataset.DatasetId, dataset.DataType.TypeId, dataset.SpaceId, hyperslabSpaceId, buffer);
	}

	private int H5DWriteInternal<T>(H5DatasetID dataset, long hyperslabSpaceId, T[,] buffer)
	{
		return H5DWriteInternal(dataset.DatasetId, dataset.DataType.TypeId, dataset.SpaceId, hyperslabSpaceId, buffer);
	}

	private static ulong[] OptimalChunking<T>(T[] values)
	{
		return OptimalChunking(values.AsSpan());
	}

	private static ulong[] OptimalChunking<T>(Span<T> values)
	{
		return OptimalChunking(bytesPerVal: (!(typeof(T) == typeof(string))) ? Marshal.SizeOf(typeof(T)) : 16, datasetLength: values.Length);
	}

	private static ulong[] OptimalChunking<T>(T[,] values)
	{
		return OptimalChunking(bytesPerVal: (!(typeof(T) == typeof(string))) ? Marshal.SizeOf(typeof(T)) : 16, datasetHeight: values.GetLength(0), datasetWidth: values.GetLength(1));
	}

	private static ulong[] OptimalChunking<T>(T[,,] values)
	{
		return OptimalChunking(bytesPerVal: (!(typeof(T) == typeof(string))) ? Marshal.SizeOf(typeof(T)) : 16, datasetDepth: values.GetLength(0), datasetHeight: values.GetLength(1), datasetWidth: values.GetLength(2));
	}

	private static ulong[] OptimalChunking(int datasetLength, int bytesPerVal)
	{
		ulong[] ulongs = new ulong[1] { (ulong)datasetLength };
		while ((ulong)((long)ulongs[0] * (long)bytesPerVal) > 1048576uL)
		{
			if (ulongs[0] % 2 == 1)
			{
				ulongs[0]++;
			}
			ulongs[0] /= 2uL;
		}
		if (ulongs[0] == 0L)
		{
			ulongs[0] = 1uL;
		}
		return ulongs;
	}

	public static ulong[] OptimalChunking<T>(int datasetHeight, int datasetWidth) where T : struct
	{
		return OptimalChunking(datasetHeight, datasetWidth, Marshal.SizeOf<T>());
	}

	private static ulong[] OptimalChunking(int datasetHeight, int datasetWidth, int bytesPerVal)
	{
		ulong[] ulongs = new ulong[2]
		{
			(ulong)datasetHeight,
			(ulong)datasetWidth
		};
		if (datasetHeight == 0 || datasetWidth == 0)
		{
			return ulongs;
		}
		int datasetWidthTimesBytesPerVal = datasetWidth * bytesPerVal;
		ulongs[0] = Convert.ToUInt64(Math.Floor(1048576.0 / (double)datasetWidthTimesBytesPerVal));
		ulongs[0] = Math.Min(ulongs[0], (ulong)datasetHeight);
		ulongs[0] = Math.Max(ulongs[0], 1uL);
		return ulongs;
	}

	private static ulong[] OptimalChunking(int datasetDepth, int datasetHeight, int datasetWidth, int bytesPerVal)
	{
		ulong[] ulongs = new ulong[3]
		{
			(ulong)datasetDepth,
			(ulong)datasetHeight,
			(ulong)datasetWidth
		};
		int datasetWidthTimesBytesPerVal = datasetWidth * bytesPerVal;
		int datasetHeightTerm = datasetWidthTimesBytesPerVal * datasetHeight;
		if (datasetHeightTerm < 1048576)
		{
			ulongs[0] = Convert.ToUInt64(Math.Floor(1048576.0 / (double)datasetHeightTerm));
			ulongs[0] = Math.Min(ulongs[0], (ulong)datasetDepth);
			ulongs[0] = Math.Max(ulongs[0], 1uL);
		}
		else
		{
			ulongs[0] = 1uL;
			ulongs[1] = Convert.ToUInt64(Math.Floor(1048576.0 / (double)datasetWidthTimesBytesPerVal));
			ulongs[1] = Math.Min(ulongs[1], (ulong)datasetHeight);
			ulongs[1] = Math.Max(ulongs[1], 1uL);
		}
		return ulongs;
	}

	private static int GetMaxStringSize(string[] values)
	{
		int maxStringSize = 1;
		for (int i = 0; i < values.GetLength(0); i++)
		{
			string textValue = values[i];
			if (textValue == null)
			{
				textValue = string.Empty;
			}
			int length = Encoding.UTF8.GetByteCount(textValue);
			if (length > maxStringSize)
			{
				maxStringSize = length;
			}
		}
		return maxStringSize;
	}

	private static int GetMaxStringSize(string[,] values)
	{
		int maxStringSize = 1;
		for (int i = 0; i < values.GetLength(0); i++)
		{
			for (int j = 0; j < values.GetLength(1); j++)
			{
				int length = Encoding.UTF8.GetByteCount(values[i, j] ?? "");
				if (length > maxStringSize)
				{
					maxStringSize = length;
				}
			}
		}
		return maxStringSize;
	}

	private int GetMaxStringSize(string[,,] values)
	{
		int maxStringSize = 1;
		for (int i = 0; i < values.GetLength(0); i++)
		{
			for (int j = 0; j < values.GetLength(1); j++)
			{
				for (int k = 0; k < values.GetLength(2); k++)
				{
					int length = Encoding.UTF8.GetByteCount(values[i, j, k] ?? "");
					if (length > maxStringSize)
					{
						maxStringSize = length;
					}
				}
			}
		}
		return maxStringSize;
	}

	private byte[] SerializePacked(string[] values)
	{
		Encoding aSCIIEncoding = Encoding.UTF8;
		int count = 0;
		for (int i = 0; i < values.Length; i++)
		{
			string textValue = values[i];
			if (textValue == null)
			{
				textValue = string.Empty;
			}
			count += Encoding.UTF8.GetByteCount(textValue) + 1;
		}
		byte[] bytesArray = new byte[count];
		int lengthOffsetSum = 0;
		for (int j = 0; j < values.Length; j++)
		{
			string textValue2 = values[j];
			if (textValue2 == null)
			{
				textValue2 = string.Empty;
			}
			byte[] bytes = aSCIIEncoding.GetBytes(textValue2);
			bytes.CopyTo(bytesArray, lengthOffsetSum);
			lengthOffsetSum += bytes.Length + 1;
		}
		return bytesArray;
	}

	private byte[] SerializeAligned(string[] values, int maxStrSize)
	{
		byte[] bytes = new byte[values.Length * maxStrSize];
		for (int i = 0; i < values.Length; i++)
		{
			string textValue = values[i];
			if (textValue == null)
			{
				textValue = string.Empty;
			}
			H5StringEncoding.WriteFixed(textValue, bytes.AsSpan(i * maxStrSize, maxStrSize));
		}
		return bytes;
	}

	private byte[,] SerializeAligned(string[,] values, int setSize)
	{
		byte[,] bytes = new byte[values.GetLength(0), checked(values.GetLength(1) * setSize)];
		byte[] buffer = new byte[setSize];
		for (int i = 0; i < values.GetLength(0); i++)
			for (int j = 0; j < values.GetLength(1); j++)
			{
				H5StringEncoding.WriteFixed(values[i, j], buffer);
				Buffer.BlockCopy(buffer, 0, bytes, (i * values.GetLength(1) + j) * setSize, setSize);
			}
		return bytes;
	}

	private byte[] SerializeAligned(string[,,] values, int setSize)
	{
		byte[] bytes = new byte[checked(values.Length * setSize)];
		int offset = 0;
		foreach (string value in values)
		{
			H5StringEncoding.WriteFixed(value, bytes.AsSpan(offset, setSize)); offset += setSize;
		}
		return bytes;
	}

	private string GetOneLevelUp(string setOrGroupName)
	{
		string newStringsTerm = "";
		string[] newStrings = setOrGroupName.Split('/');
		H5Reader.StripEmpty(ref newStrings);
		for (int i = 0; i < newStrings.Length - 1; i++)
		{
			newStringsTerm = newStringsTerm + "/" + newStrings[i];
		}
		return newStringsTerm + "/";
	}

	public void WriteStructuredLogs(string datasetName, IEnumerable<Message> messages)
	{
		List<Message> messagesList = messages.ToList();
		if (messagesList.Count == 0)
		{
			return;
		}
		List<(string, string)> valueTuples = new List<(string, string)>();
		List<int> intList = new List<int>();
		Dictionary<string, int> intByString = new Dictionary<string, int>();
		int ordinal = 0;
		foreach (Message message2 in messagesList)
		{
			if (MessageClass.IsNull(message2.MessageType))
			{
				string message3 = $"ADHOC_{ordinal:D6}";
				string evaluatedMessage = message2.EvaluatedMessage;
				ordinal++;
				intByString[message3] = valueTuples.Count;
				valueTuples.Add((message3, evaluatedMessage));
				intList.Add(intByString[message3]);
			}
			else
			{
				string code = message2.MessageType.Code;
				string evaluatedMessage = message2.MessageType.DefaultFormattedMessage;
				if (!intByString.ContainsKey(code))
				{
					intByString[code] = valueTuples.Count;
					valueTuples.Add((code, evaluatedMessage));
				}
				intList.Add(intByString[code]);
			}
		}
		int[] values = Enumerable.Range(0, messagesList.Count).ToArray();
		WriteDataset(datasetName, values);
		string[] values2 = valueTuples.Select(((string Code, string DefaultFormattedMessage) mc) => mc.Code).ToArray();
		string[] values3 = valueTuples.Select(((string Code, string DefaultFormattedMessage) mc) => mc.DefaultFormattedMessage).ToArray();
		WriteAttribute(datasetName, "MessageClasses_Code", values2);
		WriteAttribute(datasetName, "MessageClasses_DefaultFormattedMessage", values3);
		byte[] byteValues = new byte[messagesList.Count];
		int[] intValues = new int[messagesList.Count];
		string[] strings = new string[messagesList.Count];
		for (int intervalIndex = 0; intervalIndex < messagesList.Count; intervalIndex++)
		{
			Message message = messagesList[intervalIndex];
			byteValues[intervalIndex] = MapSeverityToByte(message.MessageType.DefaultSeverity);
			intValues[intervalIndex] = intList[intervalIndex];
			strings[intervalIndex] = message.EvaluatedMessage;
		}
		WriteAttribute(datasetName, "Severity", byteValues);
		WriteAttribute(datasetName, "MessageClassIndex", intValues);
		WriteAttribute(datasetName, "EvaluatedMessage", strings);
	}

	private static byte MapSeverityToByte(MessageSeverity severity)
	{
		return severity switch
		{
			MessageSeverity.Trace => (byte)0, 
			MessageSeverity.Debug => (byte)1, 
			MessageSeverity.Information => (byte)2, 
			MessageSeverity.Warning => (byte)3, 
			MessageSeverity.Error => (byte)4, 
			_ => (byte)2, 
		};
	}
}
