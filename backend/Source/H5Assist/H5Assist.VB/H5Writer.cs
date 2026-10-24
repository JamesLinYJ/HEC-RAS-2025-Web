using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using H5Assist.Chunking;
using H5Assist.Identifiers;
using HDF.PInvoke;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Memory;
using Utility.Progress;

namespace H5Assist.VB;

public class H5Writer : H5Reader
{
	public bool UseCompression;

	public const int DefaultMaxChunkSizeBytes = 1048576;

	public H5Writer(string fileName)
	{
		if (!File.Exists(fileName))
		{
			string directoryName = Path.GetDirectoryName(fileName);
			if (!Directory.Exists(directoryName) && !string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			FileId = H5F.create(fileName, 2u, 0L, 0L);
		}
		else
		{
			FileId = H5F.open(fileName, 1u, 0L);
		}
		_filename = fileName;
		UseCompression = true;
	}

	private H5Writer(string fileName, bool openReadWriteMode)
	{
		if (openReadWriteMode)
		{
			if (!File.Exists(fileName))
			{
				FileId = H5F.create(fileName, 2u, 0L, 0L);
			}
			else
			{
				FileId = H5F.open(fileName, 1u, 0L);
			}
		}
		else
		{
			if (!File.Exists(fileName))
			{
				throw new Exception("Operation failed, does " + fileName + " exist?");
			}
			FileId = H5F.open(fileName, 0u, 0L);
		}
		_filename = fileName;
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
		_ = H5Reader._log & Log.RecordOperation;
		_ = 0;
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

	public void CreateExternalLink(string externFile, string externPath, string localPath)
	{
		H5L.create_external(externFile, externPath, FileId, localPath, 0L, 0L);
	}

	public void RenameGroup(string currentGroupName, string newGroupName)
	{
		H5L.move(FileId, currentGroupName, FileId, newGroupName, 0L, 0L);
	}

	public void Delete(string groupOrDatasetName)
	{
		_ = H5Reader._log & Log.RecordOperation;
		_ = 0;
		if (ObjectExists(groupOrDatasetName))
		{
			H5L.delete(FileId, groupOrDatasetName, 0L);
		}
	}

	public void CleanGroup(string groupName)
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
		if ((H5Reader._log & Log.RecordOperation) > Log.None)
		{
			LogAttributeOperation<object>(objectName, attributeName, "WriteAttributePreCheck");
		}
		if ((H5Reader._log & Log.SafetyChecks) > Log.None)
		{
			EnsureObjectExists(objectName, "WriteAttributePreCheck");
		}
	}

	public void WriteAttribute<T>(string objectName, string attributeName, T localValue)
	{
		try
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
				ordinal = ((string)(object)localValue).Length;
				if (ordinal == 0)
				{
					ordinal = 1;
				}
			}
			using H5AttributeID attr = H5AttributeID.CreateScalar(h5ObjectID.ObjectId, attributeName, typeof(T), ordinal);
			if (condition)
			{
				byte[] bytes = new ASCIIEncoding().GetBytes((string)(object)localValue);
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
		catch (Exception ex)
		{
			Console.WriteLine("WRITE ATTR ERROR: " + ex.Message);
		}
	}

	public void WriteAttribute<T>(string objectName, string attributeName, T[] values)
	{
		try
		{
			ThrowIfNull(values, objectName, attributeName);
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
		catch (Exception ex)
		{
			Console.WriteLine("WRITE ATTR ERROR: " + ex.Message);
		}
	}

	public void WriteAttribute<T>(string objectName, string attributeName, T[,] values)
	{
		ThrowIfNull(values, objectName, attributeName);
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

	private void H5AWriteInternal<T>(long attributeId, long typeId, T[] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5A.write(attributeId, typeId, nint2);
		});
	}

	private void H5AWriteInternal<T>(long attributeId, long typeId, T[,] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5A.write(attributeId, typeId, nint2);
		});
	}

	private void H5AWriteInternal<T>(H5AttributeID attr, T[] buffer)
	{
		H5AWriteInternal(attr.AttributeId, attr.DataType.TypeId, buffer);
	}

	private void H5AWriteInternal<T>(H5AttributeID attr, T[,] buffer)
	{
		H5AWriteInternal(attr.AttributeId, attr.DataType.TypeId, buffer);
	}

	private void WriteDatasetPreCheck(string datasetName, [CallerMemberName] string callerName = "")
	{
		_ = H5Reader._log & Log.RecordOperation;
		_ = 0;
		CreateGroup(GetOneLevelUp(datasetName));
		if (ExistsAsDataset(datasetName) || ExistsAsGroup(datasetName))
		{
			Delete(datasetName);
		}
	}

	private void AppendDatasetPreCheck(string datasetName, [CallerMemberName] string callerName = "")
	{
		_ = H5Reader._log & Log.RecordOperation;
		_ = 0;
		if ((H5Reader._log & Log.SafetyChecks) > Log.None)
		{
			EnsureDatasetExists(datasetName, callerName);
		}
	}

	private void InsertDatasetPreCheck(string datasetName, [CallerMemberName] string callerName = "")
	{
		AppendDatasetPreCheck(datasetName, callerName);
	}

	public void WriteArray<T>(string datasetName, T[] values)
	{
		ThrowIfNull(values, datasetName);
		WriteDatasetPreCheck(datasetName, "WriteArray");
		ulong[] size = new ulong[1] { (ulong)values.Length };
		ulong[] chunking = OptimalChunking(values);
		bool condition = typeof(T) == typeof(string);
		int maxStringSize = 0;
		if (condition)
		{
			maxStringSize = GetMaxStringSize((string[])(object)values);
		}
		using H5DatasetID dataset = H5DatasetID.CreateBasic(FileId, datasetName, typeof(T), size, chunking, maxStringSize);
		if (condition)
		{
			H5DWriteInternal(dataset, SerializeAligned((string[])(object)values, maxStringSize));
		}
		else
		{
			H5DWriteInternal(dataset, values);
		}
	}

	public void WriteArray<T>(string datasetName, T[,] values)
	{
		ThrowIfNull(values, datasetName);
		WriteDatasetPreCheck(datasetName, "WriteArray");
		ulong[] size = new ulong[2]
		{
			(ulong)values.GetLength(0),
			(ulong)values.GetLength(1)
		};
		ulong[] chunking = OptimalChunking(values);
		bool condition = typeof(T) == typeof(string);
		int setSize = 0;
		if (condition)
		{
			setSize = GetMaxStringSize((string[,])(object)values);
		}
		using H5DatasetID dataset = H5DatasetID.CreateBasic(FileId, datasetName, typeof(T), size, chunking, setSize);
		if (condition)
		{
			H5DWriteInternal(dataset, SerializeAligned((string[,])(object)values, setSize));
		}
		else
		{
			H5DWriteInternal(dataset, values);
		}
	}

	public void WriteArray<T>(string datasetName, T[,,] values)
	{
		ThrowIfNull(values, datasetName);
		WriteDatasetPreCheck(datasetName, "WriteArray");
		ulong[] size = new ulong[3]
		{
			(ulong)values.GetLength(0),
			(ulong)values.GetLength(1),
			(ulong)values.GetLength(2)
		};
		ulong[] chunking = OptimalChunking(values);
		bool condition = typeof(T) == typeof(string);
		int setSize = 0;
		if (condition)
		{
			setSize = GetMaxStringSize((string[,,])(object)values);
		}
		using H5DatasetID dataset = H5DatasetID.CreateBasic(FileId, datasetName, typeof(T), size, chunking, setSize);
		if (condition)
		{
			H5DWriteInternal(dataset, SerializeAligned((string[,,])(object)values, setSize));
		}
		else
		{
			H5DWriteInternal(dataset, values);
		}
	}

	public void WriteUncompressed<T>(string datasetName, T[] values)
	{
		ThrowIfNull(values, datasetName);
		WriteDatasetPreCheck(datasetName, "WriteUncompressed");
		ulong[] size = new ulong[1] { (ulong)values.Length };
		if (typeof(T) == typeof(string))
		{
			throw new NotSupportedException();
		}
		using H5DatasetID dataset = H5DatasetID.CreateBasicUncompressed(FileId, datasetName, typeof(T), size);
		H5DWriteInternal(dataset, values);
	}

	public void Create2dExtendibleDataset(string datasetName, Type type, int numRowsPerChunk, int numColsPerChunk)
	{
		WriteDatasetPreCheck(datasetName, "Create2dExtendibleDataset");
		if (type == typeof(string))
		{
			throw new NotImplementedException();
		}
		using (H5DatasetID.CreateExtendible(chunking: new ulong[2]
		{
			(ulong)numRowsPerChunk,
			(ulong)numColsPerChunk
		}, fileId: FileId, datasetName: datasetName, type: type))
		{
		}
	}

	public void Create2dExtendibleDataset<T>(string datasetName, int numRowsPerChunk, int numColsPerChunk)
	{
		Create2dExtendibleDataset(datasetName, typeof(T), numRowsPerChunk, numColsPerChunk);
	}

	public void Create1dExtendibleDataset<T>(string datasetName, int numElementsPerChunk)
	{
		Create1dExtendibleDataset(datasetName, typeof(T), numElementsPerChunk);
	}

	public void Create1dExtendibleDataset(string datasetName, Type type, int numElementsPerChunk)
	{
		WriteDatasetPreCheck(datasetName, "Create1dExtendibleDataset");
		if (type == typeof(string))
		{
			throw new NotImplementedException();
		}
		using (H5DatasetID.CreateExtendible(chunking: new ulong[1] { (ulong)numElementsPerChunk }, fileId: FileId, datasetName: datasetName, type: type))
		{
		}
	}

	public void Create1dExtendibleStringDataset(string datasetName, int numElementsPerChunk, int maxStringSize)
	{
		WriteDatasetPreCheck(datasetName, "Create1dExtendibleStringDataset");
		using (H5DatasetID.CreateExtendible(chunking: new ulong[1] { (ulong)numElementsPerChunk }, fileId: FileId, datasetName: datasetName, type: typeof(string), setSize: maxStringSize))
		{
		}
	}

	public void Create2dExtendibleStringDataset(string datasetName, int numRowsPerChunk, int numColsPerChunk, int maxStringSize)
	{
		WriteDatasetPreCheck(datasetName, "Create2dExtendibleStringDataset");
		ulong[] ulongs = new ulong[1];
		ulongs[0] = (ulong)numRowsPerChunk;
		ulongs[1] = (ulong)numColsPerChunk;
		using (H5DatasetID.CreateExtendible(FileId, datasetName, typeof(string), ulongs, maxStringSize))
		{
		}
	}

	public void Create3dExtendibleDataset(string datasetName, Type type, int numRowsPerChunk, int numColsPerChunk, int numPlanesPerChunk)
	{
		WriteDatasetPreCheck(datasetName, "Create3dExtendibleDataset");
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

	public void AddStringElements(string datasetName, string[] values, int elementsToWrite = int.MinValue)
	{
		ThrowIfNull(values, datasetName);
		AppendDatasetPreCheck(datasetName, "AddStringElements");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ulong[] ulongs = new ulong[1];
		if (elementsToWrite == int.MinValue)
		{
			ulongs[0] = (ulong)values.Length;
		}
		else
		{
			ulongs[0] = (ulong)elementsToWrite;
		}
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendElements(h5DatasetID, ulongs);
		int size = h5DatasetID.DataType.GetSize();
		byte[] buffer = SerializeAligned(values, size);
		H5DWriteInternal(h5DatasetID.DatasetId, h5DatasetID.DataType.TypeId, h5DatasetID.SpaceId, h5HyperslabID.SpaceId, buffer);
	}

	public void Create1DCompoundDataset(string datasetName, int numElementsPerChunk, CompoundTypeDefinition compoundType)
	{
		WriteDatasetPreCheck(datasetName, "Create1DCompoundDataset");
		using (H5DatasetID.CreateCompound(chunking: new ulong[1] { (ulong)numElementsPerChunk }, fileId: FileId, datasetName: datasetName, compoundType: compoundType))
		{
		}
	}

	public void AddCompoundElement(string datasetName, object[] row)
	{
		ThrowIfNull(row, datasetName);
		AppendDatasetPreCheck(datasetName, "AddCompoundElement");
		using H5DatasetID h5DatasetID = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendElements(h5DatasetID, new ulong[1] { 1uL });
		H5DWriteInternal(h5DatasetID, h5HyperslabID.SpaceId, ByteifyObjects(row, h5DatasetID.DataType));
	}

	public void AddBuffer1D(string datasetName, byte[] buffer)
	{
		ThrowIfNull(buffer, datasetName);
		AppendDatasetPreCheck(datasetName, "AddBuffer1D");
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
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
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
				if (textValue.Length > size)
				{
					aSCIIEncoding.GetBytes(textValue.Substring(0, size)).CopyTo(bytes, 0);
				}
				else
				{
					aSCIIEncoding.GetBytes(textValue).CopyTo(bytes, 0);
				}
			}
			Buffer.BlockCopy(bytes, 0, byteValues, byteLengthSum, bytes.Length);
			byteLengthSum += bytes.Length;
		}
		return byteValues;
	}

	public void AddElements<T>(string datasetName, T[] values, int elementsToWrite = int.MinValue)
	{
		ThrowIfNull(values, datasetName);
		AppendDatasetPreCheck(datasetName, "AddElements");
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		ulong[] ulongs = new ulong[1];
		if (elementsToWrite == int.MinValue)
		{
			ulongs[0] = (ulong)values.Length;
		}
		else
		{
			ulongs[0] = (ulong)elementsToWrite;
		}
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendElements(dataset, ulongs);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void InsertChunk<T>(string datasetName, T[] values, int chunkIdx)
	{
		ThrowIfNull(values, datasetName);
		InsertDatasetPreCheck(datasetName, "InsertChunk");
		long getChunkSizeDatasetNameTerm = GetChunkSize(datasetName)[0] * chunkIdx;
		InsertValues(datasetName, values, (int)getChunkSizeDatasetNameTerm);
	}

	public void InsertChunk<T>(string datasetName, T[,] values, int chunkRowIdx, int chunkColIdx)
	{
		ThrowIfNull(values, datasetName);
		InsertDatasetPreCheck(datasetName, "InsertChunk");
		long[] chunkSize = GetChunkSize(datasetName);
		long chunkSizeChunkRowIdxTerm = chunkSize[0] * chunkRowIdx;
		long chunkSizeChunkColIdxTerm = chunkSize[1] * chunkColIdx;
		InsertValues(datasetName, values, (int)chunkSizeChunkRowIdxTerm, (int)chunkSizeChunkColIdxTerm);
	}

	public void InsertValues<T>(string datasetName, T[] values, int startIdx)
	{
		ThrowIfNull(values, datasetName);
		InsertDatasetPreCheck(datasetName, "InsertValues");
		if (typeof(T) == typeof(string))
		{
			throw new NotImplementedException();
		}
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.Insert1D(dataset, new ulong[1] { (ulong)values.Length }, startIdx);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void InsertValues<T>(string datasetName, T[,] values, int startRow, int startCol)
	{
		ThrowIfNull(values, datasetName);
		InsertDatasetPreCheck(datasetName, "InsertValues");
		if (typeof(T) == typeof(string))
		{
			throw new NotImplementedException();
		}
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
		ThrowIfNull(values, datasetName);
		AppendDatasetPreCheck(datasetName, "AddColumns");
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
		ThrowIfNull(values, datasetName);
		AppendDatasetPreCheck(datasetName, "AddColumn");
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
		ThrowIfNull(values, datasetName);
		AppendDatasetPreCheck(datasetName, "AddRow");
		using H5DatasetID dataset = OpenDataset(datasetName);
		AddRow(dataset, values);
	}

	public void AddRow<T>(H5DatasetID dataset, T[] values)
	{
		ThrowIfNull(values, dataset.DatasetName);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendRows(dataset, new ulong[2]
		{
			1uL,
			(ulong)values.Length
		});
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddRow<T>(string datasetName, Memory<T> values)
	{
		AppendDatasetPreCheck(datasetName, "AddRow");
		using H5DatasetID dataset = OpenDataset(datasetName);
		AddRow(dataset, values);
	}

	public void AddRow<T>(H5DatasetID dataset, Memory<T> values)
	{
		using H5HyperslabID h5HyperslabID = H5HyperslabID.AppendRows(dataset, new ulong[2]
		{
			1uL,
			(ulong)values.Length
		});
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddRow<T>(string datasetName, Memory<T> values, int rowIndex)
	{
		AppendDatasetPreCheck(datasetName, "AddRow");
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.InsertRows(dataset, new ulong[2]
		{
			1uL,
			(ulong)values.Length
		}, rowIndex);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void AddRows<T>(string datasetName, T[,] values, int rowsToWrite = int.MinValue)
	{
		ThrowIfNull(values, datasetName);
		AppendDatasetPreCheck(datasetName, "AddRows");
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

	public void AddRows<T>(string datasetName, T[,] values, int rowsToWrite, int dstStartRow)
	{
		ThrowIfNull(values, datasetName);
		AppendDatasetPreCheck(datasetName, "AddRows");
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
		ThrowIfNull(values, datasetName);
		AppendDatasetPreCheck(datasetName, "AddRow");
		using H5DatasetID dataset = H5DatasetID.Open(FileId, datasetName, needType: true, needSpace: true);
		using H5HyperslabID h5HyperslabID = H5HyperslabID.InsertRows(dataset, new ulong[2]
		{
			1uL,
			(ulong)values.Length
		}, rowIndex);
		H5DWriteInternal(dataset, h5HyperslabID.SpaceId, values);
	}

	public void Rechunk2D<T>(string datasetName, int newChunkRowCount)
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

	public void AddRows<T>(H5DatasetID dataset, T[,] values, int rowsToWrite, int dstStartRow)
	{
		ThrowIfNull(values, dataset.DatasetName);
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

	public void AddRows<T>(H5DatasetID dataset, T[,] values, int rowsToWrite = int.MinValue)
	{
		ThrowIfNull(values, dataset.DatasetName);
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

	public unsafe void TestDirectChunkWriting(string dsetName, float[] values)
	{
		ulong[] size = new ulong[1] { (ulong)values.Length };
		ulong[] chunking = new ulong[1] { (ulong)values.Length };
		H5DatasetID h5DatasetID = H5DatasetID.CreateBasic(FileId, dsetName, typeof(float), size, chunking);
		byte[] byteValues = new byte[values.Length * 4];
		Buffer.BlockCopy(values, 0, byteValues, 0, byteValues.Length);
		using (MemoryStream memoryStream = new MemoryStream(byteValues))
		{
			using MemoryStream memoryStream2 = new MemoryStream();
			using DeflateStream deflateStream = new DeflateStream(memoryStream2, CompressionLevel.Fastest, leaveOpen: true);
			memoryStream.CopyTo(deflateStream);
			deflateStream.Close();
			int memoryStream2Length = (int)memoryStream2.Length;
			byte[] buffer = memoryStream2.GetBuffer();
			ulong[] ulongs = new ulong[1];
			fixed (byte* buf = buffer)
			{
				H5DO.write_chunk(h5DatasetID.DatasetId, 0L, 0u, ref ulongs[0], memoryStream2Length, (nint)buf);
			}
		}
		h5DatasetID.Dispose();
		float[] values2 = null;
		ReadDataset(dsetName, ref values2);
	}

	public void AddStringRows(string datasetName, string[,] values, int rowsToWrite = int.MinValue)
	{
		ThrowIfNull(values, datasetName);
		AppendDatasetPreCheck(datasetName, "AddStringRows");
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

	public void WriteJagged<T, K>(string infoDataset, string valueDataset, IList<T> elements, int valsPerPoint, Func<T, int> getPointCount, Action<T, int, K[]> getPointData) where K : unmanaged
	{
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
		Create2dExtendibleDataset<int>(infoDataset, Math.Min(elements.Count, 256), 2);
		int numRowsPerChunk = H5Reader.RowChunkSize(sum, valsPerPoint, 18, bytesPerValue);
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
		ThrowIfNull(values, datasetName);
		WriteDatasetPreCheck(datasetName, "WriteVariableLenStringArray");
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
				lengthOffsetSum += textValue.Length + 1;
			}
			H5DWriteInternal(dataset, nints);
		}
	}

	public unsafe void WriteVariableLenArray<T>(string datasetName, T[][] values) where T : unmanaged
	{
		ThrowIfNull(values, datasetName);
		WriteDatasetPreCheck(datasetName, "WriteVariableLenArray");
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

	public void CopyTo(string sourceObject, string destinationObject)
	{
		_ = H5Reader._log & Log.RecordOperation;
		_ = 0;
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
		_ = H5Reader._log & Log.RecordOperation;
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
				H5Reader.Transfer<string>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(int))
			{
				H5Reader.Transfer<int>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(long))
			{
				H5Reader.Transfer<long>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(float))
			{
				H5Reader.Transfer<float>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(double))
			{
				H5Reader.Transfer<double>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			if (type == typeof(bool))
			{
				H5Reader.Transfer<bool>(this, this, sourceObject, destinationObject, key, nDims);
				continue;
			}
			throw new NotImplementedException();
		}
	}

	public void AddComment(H5DatasetID dataset, string comment)
	{
		throw new NotImplementedException();
	}

	public unsafe void WriteChunkDirect(H5DatasetID dataset, int[] chunkIndex, float[] values)
	{
		fixed (float* localValue = values)
		{
			byte* buffer = (byte*)localValue;
			WriteChunkDirect_Internal(dataset, chunkIndex, buffer, values.Length * 4);
		}
	}

	public unsafe void WriteChunkDirect(H5DatasetID dataset, int[] chunkIndex, float[,] values)
	{
		fixed (float* localValue = values)
		{
			byte* buffer = (byte*)localValue;
			WriteChunkDirect_Internal(dataset, chunkIndex, buffer, values.Length * 4);
		}
	}

	private unsafe void WriteChunkDirect_Internal(H5DatasetID dataset, int[] chunkIndex, byte* buffer, int bufLen)
	{
		uint filter_mask = 0u;
		long[] chunkSize = GetChunkSize(dataset);
		ulong[] elementIndex = new ulong[chunkIndex.Length];
		for (int i = 0; i < chunkIndex.Length; i++)
		{
			elementIndex[i] = (ulong)(chunkIndex[i] * chunkSize[i]);
		}
		byte[] buf = null;
		int validLen = 0;
		using (UnmanagedMemoryStream dataStream = new UnmanagedMemoryStream(buffer, bufLen))
		{
			CompressTile_Reuse_ZlibHeaderAdlerSpace(dataStream, out buf, out validLen);
		}
		buf[0] = 88;
		buf[1] = 133;
		Span<byte> byteSpan = new Span<byte>(buffer, bufLen);
		Adler32.ComputeAdler32(result: new Span<byte>(buf, validLen - 4, 4), buffer: byteSpan);
		long[] datasetDimensions = GetDatasetDimensions(dataset);
		ulong[] ulongs = new ulong[datasetDimensions.Length];
		bool condition = false;
		for (int j = 0; j < datasetDimensions.Length; j++)
		{
			long datasetDimension = datasetDimensions[j];
			long ulongs2 = (chunkIndex[j] + 1) * chunkSize[j];
			if (datasetDimension < ulongs2)
			{
				ulongs[j] = (ulong)ulongs2;
				condition = true;
			}
			else
			{
				ulongs[j] = (ulong)datasetDimension;
			}
		}
		if (condition)
		{
			SetExtent(dataset, ulongs);
		}
		PinPointer(buf, (nint nint2) =>
		{
			Advanced.write_chunk(dataset.DatasetId, 0L, filter_mask, elementIndex, validLen, nint2);
		});
	}

	public unsafe void WriteChunkDirect_Threadsafe<T>(H5DatasetID dataset, int[] chunkIndex, T[] values) where T : struct
	{
		PinPointer(values, (nint iptr) =>
		{
			WriteChunkDirect_Internal_Threadsafe(dataset, chunkIndex, (byte*)iptr, values.Length * Marshal.SizeOf<T>());
		});
	}

	public unsafe void WriteChunkDirect_Threadsafe<T>(H5DatasetID dataset, int[] chunkIndex, T[,] values) where T : struct
	{
		PinPointer(values, (nint iptr) =>
		{
			WriteChunkDirect_Internal_Threadsafe(dataset, chunkIndex, (byte*)iptr, values.Length * Marshal.SizeOf<T>());
		});
	}

	private unsafe void WriteChunkDirect_Internal_Threadsafe(H5DatasetID dataset, int[] chunkIndex, byte* buffer, int bufLen)
	{
		uint filter_mask = 0u;
		long[] cachedChunkSize = GetCachedChunkSize(dataset);
		ulong[] elementIndex = new ulong[chunkIndex.Length];
		for (int i = 0; i < chunkIndex.Length; i++)
		{
			elementIndex[i] = (ulong)(chunkIndex[i] * cachedChunkSize[i]);
		}
		byte[] buf = null;
		int validLen = 0;
		using (UnmanagedMemoryStream dataStream = new UnmanagedMemoryStream(buffer, bufLen))
		{
			CompressTile_Reuse_ZlibHeaderAdlerSpace(dataStream, out buf, out validLen);
		}
		buf[0] = 88;
		buf[1] = 133;
		Span<byte> result = buf.AsSpan().Slice(validLen - 4);
		Adler32.ComputeAdler32(new Span<byte>(buffer, bufLen), result);
		if (H5Reader.IsSafetyChecksOn)
		{
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
		}
		PinPointer(buf, (nint nint2) =>
		{
			Advanced.write_chunk(dataset.DatasetId, 0L, filter_mask, elementIndex, validLen, nint2);
		});
	}

	public void WriteParallelByRow<T>(string dataset, Func<int, T[]> getRow) where T : struct
	{
		using H5DatasetID dataset2 = OpenDataset(dataset);
		WriteParallelByRow(dataset2, getRow);
	}

	public unsafe void WriteParallelByRow<T>(H5DatasetID dataset, Func<int, T[]> getRow, ProgressReporter prog = null) where T : struct
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
			int[] chunkArr = new int[2];
			chunkArr[0] = chunkIdx;
			chunkArr[1] = 0;
			PinPointer(tlChunkBufferTerm, (nint iptr) =>
			{
				WriteChunkDirect_Internal_Threadsafe(dataset, chunkArr, (byte*)iptr, chunkSizeBytes);
			});
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

	private static void CompressTile(Stream dataStream, out byte[] buf, out int bufLen)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionLevel.Fastest, leaveOpen: true);
		dataStream.CopyTo(deflateStream);
		deflateStream.Close();
		bufLen = (int)memoryStream.Length;
		buf = memoryStream.GetBuffer();
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

	public void Flush()
	{
		_ = H5Reader._log & Log.RecordOperation;
		_ = 0;
		H5F.flush(FileId, H5F.scope_t.GLOBAL);
	}

	private void H5DWriteInternal<T>(long datasetId, long typeId, T[] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.write(datasetId, typeId, 0L, 0L, 0L, nint2);
		});
	}

	private void H5DWriteInternal<T>(long datasetId, long typeId, T[,] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.write(datasetId, typeId, 0L, 0L, 0L, nint2);
		});
	}

	private void H5DWriteInternal<T>(long datasetId, long typeId, T[,,] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.write(datasetId, typeId, 0L, 0L, 0L, nint2);
		});
	}

	private void H5DWriteInternal<T>(H5DatasetID dataset, T[] buffer)
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

	private unsafe void H5DWriteInternal<T>(long datasetId, long typeId, Memory<T> buffer)
	{
		using MemoryHandle memoryHandle = buffer.Pin();
		H5D.write(datasetId, typeId, 0L, 0L, 0L, new IntPtr(memoryHandle.Pointer));
	}

	private unsafe void H5DWriteInternal<T>(H5DatasetID dataset, long hyperslabSpaceId, Memory<T> buffer)
	{
		using MemoryHandle memoryHandle = buffer.Pin();
		H5D.write(dataset.DatasetId, dataset.DataType.TypeId, hyperslabSpaceId, dataset.SpaceId, 0L, new IntPtr(memoryHandle.Pointer));
	}

	private void H5DWriteInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, T[] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.write(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, nint2);
		});
	}

	private void H5DWriteInternal<T>(long datasetId, long typeId, long spaceId, long hyperslabSpaceId, T[,] buffer)
	{
		PinPointer(buffer, (nint nint2) =>
		{
			H5D.write(datasetId, typeId, hyperslabSpaceId, spaceId, 0L, nint2);
		});
	}

	private void H5DWriteInternal<T>(H5DatasetID dataset, long hyperslabSpaceId, T[] buffer)
	{
		H5DWriteInternal(dataset.DatasetId, dataset.DataType.TypeId, dataset.SpaceId, hyperslabSpaceId, buffer);
	}

	private void H5DWriteInternal<T>(H5DatasetID dataset, long hyperslabSpaceId, T[,] buffer)
	{
		H5DWriteInternal(dataset.DatasetId, dataset.DataType.TypeId, dataset.SpaceId, hyperslabSpaceId, buffer);
	}

	private static ulong[] OptimalChunking<T>(T[] values)
	{
		return OptimalChunking(bytesPerVal: (!(typeof(T) == typeof(string))) ? Marshal.SizeOf(typeof(T)) : 16, datasetLength: values.GetLength(0));
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
			int length = textValue.Length;
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
				int length = values[i, j].Length;
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
					int length = values[i, j, k].Length;
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
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		int count = 0;
		for (int i = 0; i < values.Length; i++)
		{
			string textValue = values[i];
			if (textValue == null)
			{
				textValue = string.Empty;
			}
			count += textValue.Length + 1;
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
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		byte[] bytes = new byte[values.Length * maxStrSize];
		for (int i = 0; i < values.Length; i++)
		{
			string textValue = values[i];
			if (textValue == null)
			{
				textValue = string.Empty;
			}
			byte[] bytesArray = ((textValue.Length <= maxStrSize) ? aSCIIEncoding.GetBytes(textValue) : aSCIIEncoding.GetBytes(textValue.Substring(0, maxStrSize)));
			bytesArray.CopyTo(bytes, i * maxStrSize);
		}
		return bytes;
	}

	private byte[,] SerializeAligned(string[,] values, int setSize)
	{
		ASCIIEncoding aSCIIEncoding = new ASCIIEncoding();
		byte[,] byteValues = new byte[values.GetLength(0), values.GetLength(1) * setSize];
		for (int i = 0; i < values.GetLength(0); i++)
		{
			for (int j = 0; j < values.GetLength(1); j++)
			{
				byte[] bytes = aSCIIEncoding.GetBytes(values[i, j]);
				int k;
				for (k = 0; k < bytes.GetLength(0); k++)
				{
					byteValues[i, j * setSize + k] = bytes[k];
				}
				for (int l = k; l < setSize; l++)
				{
					byteValues[i, j * setSize + l] = 0;
				}
			}
		}
		return byteValues;
	}

	private byte[] SerializeAligned(string[,,] values, int setSize)
	{
		throw new NotImplementedException(values?.ToString() ?? setSize.ToString());
	}

	private void ThrowIfNull<T>(T arr, string name1) where T : class
	{
		if (arr == null)
		{
			throw new ArgumentNullException("Cannot write null value to " + name1);
		}
	}

	private void ThrowIfNull<T>(T arr, string name1, string name2) where T : class
	{
		if (arr == null)
		{
			throw new ArgumentNullException("Array", "Cannot write null value to " + name1 + ": " + name2);
		}
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
}
