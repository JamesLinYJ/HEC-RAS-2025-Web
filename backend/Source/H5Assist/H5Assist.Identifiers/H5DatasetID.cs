using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HDF.PInvoke;
using Microsoft.Extensions.Logging;

namespace H5Assist.Identifiers;

public sealed class H5DatasetID : IDisposable
{
	private static readonly ILogger _logger = H5Reader.Logger;

	private const long NULLID = long.MinValue;

	public long DatasetId = long.MinValue;

	public H5DataTypeID DataType;

	public long SpaceId = long.MinValue;

	public long CreatePropertyListId = long.MinValue;

	public long AccessPropertyListId = long.MinValue;

	public string DatasetName;

	public static bool TryOpen(long fileId, string datasetName, out H5DatasetID dset)
	{
		long accessPropertyListId = H5P.create(H5P.DATASET_ACCESS);
		long datasetId = H5D.open(fileId, datasetName, accessPropertyListId);
		if (datasetId < 0)
		{
			H5P.close(accessPropertyListId);
			dset = null;
			return false;
		}
		dset = new H5DatasetID
		{
			DatasetName = datasetName,
			DatasetId = datasetId,
			AccessPropertyListId = accessPropertyListId
		};
		dset.DataType = H5DataTypeID.Open(dset);
		dset.SpaceId = H5D.get_space(dset.DatasetId);
		dset.CreatePropertyListId = H5D.get_create_plist(dset.DatasetId);
		return true;
	}

	public static H5DatasetID Open(long fileId, string datasetName, bool needType, bool needSpace, bool needCreatePropertyList = false, int chunkCacheSizeBytes = 0)
	{
		H5DatasetID h5DatasetID = new H5DatasetID
		{
			DatasetName = datasetName,
			AccessPropertyListId = H5P.create(H5P.DATASET_ACCESS)
		};
		if (chunkCacheSizeBytes > 0)
		{
			h5DatasetID.SetChunkSize(chunkCacheSizeBytes);
		}
		h5DatasetID.DatasetId = H5D.open(fileId, h5DatasetID.DatasetName, h5DatasetID.AccessPropertyListId);
		if (h5DatasetID.DatasetId < 0)
		{
			_logger.LogError("{Dataset} was not opened successfully. {ErrorCode}", h5DatasetID.DatasetName, h5DatasetID.DatasetId);
			throw new Exception("Error opening dataset: " + h5DatasetID.DatasetName);
		}
		if (needType)
		{
			h5DatasetID.DataType = H5DataTypeID.Open(h5DatasetID);
		}
		if (needSpace)
		{
			h5DatasetID.SpaceId = H5D.get_space(h5DatasetID.DatasetId);
		}
		if (needCreatePropertyList)
		{
			h5DatasetID.CreatePropertyListId = H5D.get_create_plist(h5DatasetID.DatasetId);
		}
		return h5DatasetID;
	}

	private void SetChunkSize(int sizeInBytes, int numSlots = 511)
	{
		H5P.set_chunk_cache(AccessPropertyListId, numSlots, sizeInBytes, 1.0);
	}

	public void GetNativeIDs(out long datasetID, out long datatypeID, out long dataspaceID)
	{
		datasetID = DatasetId;
		datatypeID = DataType.TypeId;
		dataspaceID = SpaceId;
	}

	public static H5DatasetID CreateBasic(long fileId, string datasetName, Type type, ulong[] size, ulong[] chunking, int setSize = 0)
	{
		H5DatasetID h5DatasetID = new H5DatasetID
		{
			DatasetName = datasetName,
			DataType = new H5DataTypeID(type, setSize),
			SpaceId = H5S.create_simple(size.Length, size, null),
			CreatePropertyListId = H5P.create(H5P.DATASET_CREATE)
		};
		if (chunking.Any((ulong c) => c == 0))
		{
			_logger.LogWarning("Chunk Size Error <= 0: {dset}", datasetName);
		}
		H5P.set_chunk(h5DatasetID.CreatePropertyListId, chunking.Length, chunking);
		H5P.set_deflate(h5DatasetID.CreatePropertyListId, 1u);
		h5DatasetID.DatasetId = H5D.create(fileId, datasetName, h5DatasetID.DataType.TypeId, h5DatasetID.SpaceId, 0L, h5DatasetID.CreatePropertyListId, 0L);
		return h5DatasetID;
	}

	public static H5DatasetID CreateBasicUncompressed(long fileId, string datasetName, Type type, ulong[] size, int setSize = 0)
	{
		H5DatasetID h5DatasetID = new H5DatasetID
		{
			DatasetName = datasetName,
			DataType = new H5DataTypeID(type, setSize),
			SpaceId = H5S.create_simple(size.Length, size, null)
		};
		h5DatasetID.DatasetId = H5D.create(fileId, datasetName, h5DatasetID.DataType.TypeId, h5DatasetID.SpaceId, 0L, 0L, 0L);
		return h5DatasetID;
	}

	public static H5DatasetID CreateUncompressedVlen<T>(long fileId, string datasetName, ulong[] size)
	{
		H5DatasetID h5DatasetID = new H5DatasetID
		{
			DatasetName = datasetName,
			DataType = H5DataTypeID.CreateVLen<T>(),
			SpaceId = H5S.create_simple(size.Length, size, null)
		};
		h5DatasetID.DatasetId = H5D.create(fileId, datasetName, h5DatasetID.DataType.TypeId, h5DatasetID.SpaceId, 0L, 0L, 0L);
		return h5DatasetID;
	}

	public static H5DatasetID CreateBasic(H5Reader reader, string datasetName, Type type, ulong[] size, ulong[] chunking, int setSize = 0)
	{
		return CreateBasic(reader.H5FileID, datasetName, type, size, chunking, setSize);
	}

	internal static H5DatasetID CreateExtendible(long fileId, string datasetName, Type type, ulong[] chunking, int setSize = 0)
	{
		H5DataTypeID h5DataTypeID = new H5DataTypeID(type, setSize);
		long spaceId = CreateBlankSpaceID(chunking.Length);
		long createPropertyListId = H5P.create(H5P.DATASET_CREATE);
		if (chunking.Any((ulong c) => c == 0))
		{
			_logger.LogWarning("Chunk Size Error <= 0: {dset}", datasetName);
		}
		H5P.set_chunk(createPropertyListId, chunking.Length, chunking);
		H5P.set_deflate(createPropertyListId, 1u);
		SetDefaultFillValue(type, createPropertyListId, h5DataTypeID.TypeId);
		long datasetId = H5D.create(fileId, datasetName, h5DataTypeID.TypeId, spaceId, 0L, createPropertyListId, 0L);
		return new H5DatasetID
		{
			DatasetId = datasetId,
			DataType = h5DataTypeID,
			SpaceId = spaceId,
			CreatePropertyListId = createPropertyListId
		};
	}

	internal static H5DatasetID CreateExtendible<T>(long fileId, string datasetName, ulong[] chunking, T fillValue = default(T)) where T : unmanaged
	{
		H5DataTypeID h5DataTypeID = new H5DataTypeID(typeof(T));
		long spaceId = CreateBlankSpaceID(chunking.Length);
		long createPropertyListId = H5P.create(H5P.DATASET_CREATE);
		if (chunking.Any((ulong c) => c == 0))
		{
			_logger.LogWarning("Chunk Size Error <= 0: {dset}", datasetName);
		}
		H5P.set_chunk(createPropertyListId, chunking.Length, chunking);
		H5P.set_deflate(createPropertyListId, 1u);
		SetFillValue(createPropertyListId, h5DataTypeID.TypeId, fillValue);
		long datasetId = H5D.create(fileId, datasetName, h5DataTypeID.TypeId, spaceId, 0L, createPropertyListId, 0L);
		return new H5DatasetID
		{
			DatasetId = datasetId,
			DataType = h5DataTypeID,
			SpaceId = spaceId,
			CreatePropertyListId = createPropertyListId
		};
	}

	private unsafe static long CreateBlankSpaceID(int ndims)
	{
		ulong* ndimsTerm = stackalloc ulong[ndims];
		ulong* ndimsTerm2 = stackalloc ulong[ndims];
		for (int i = 0; i < ndims; i++)
		{
			ndimsTerm[i] = 0uL;
			ndimsTerm2[i] = ulong.MaxValue;
		}
		return H5S.create_simple(ndims, ndimsTerm, ndimsTerm2);
	}

	public static H5DatasetID CreateCompound(long fileId, string datasetName, CompoundTypeDefinition compoundType, ulong[] chunking)
	{
		H5DataTypeID h5DataTypeID = H5DataTypeID.CreateCompound(compoundType);
		long spaceId = CreateBlankSpaceID(chunking.Length);
		long createPropertyListId = H5P.create(H5P.DATASET_CREATE);
		if (chunking.Any((ulong c) => c == 0))
		{
			_logger.LogWarning("Chunk Size Error <= 0: {dset}", datasetName);
		}
		H5P.set_chunk(createPropertyListId, chunking.Length, chunking);
		H5P.set_deflate(createPropertyListId, 1u);
		long datasetId = H5D.create(fileId, datasetName, h5DataTypeID.TypeId, spaceId, 0L, createPropertyListId, 0L);
		return new H5DatasetID
		{
			DatasetId = datasetId,
			DataType = h5DataTypeID,
			SpaceId = spaceId,
			CreatePropertyListId = createPropertyListId
		};
	}

	public static void CreateCompound(H5Reader reader, string datasetName, CompoundTypeDefinition compoundType, ulong[] chunking)
	{
		CreateCompound(reader.H5FileID, datasetName, compoundType, chunking);
	}

	private unsafe static void SetFillValue<T>(long createPropertyListId, long typeId, T localValue) where T : unmanaged
	{
		H5P.set_fill_value(createPropertyListId, typeId, (nint)Unsafe.AsPointer(in localValue));
	}

	private unsafe static void SetDefaultFillValue(Type type, long createPropertyListId, long typeId)
	{
		if (type == typeof(float))
		{
			float naN = float.NaN;
			H5P.set_fill_value(createPropertyListId, typeId, (nint)Unsafe.AsPointer(in naN));
		}
		else if (type == typeof(double))
		{
			double naN2 = double.NaN;
			H5P.set_fill_value(createPropertyListId, typeId, (nint)Unsafe.AsPointer(in naN2));
		}
		else if (type == typeof(short))
		{
			short minValue = short.MinValue;
			H5P.set_fill_value(createPropertyListId, typeId, (nint)Unsafe.AsPointer(in minValue));
		}
		else if (type == typeof(int))
		{
			int minValue2 = int.MinValue;
			H5P.set_fill_value(createPropertyListId, typeId, (nint)Unsafe.AsPointer(in minValue2));
		}
		else if (type == typeof(long))
		{
			long minValue3 = long.MinValue;
			H5P.set_fill_value(createPropertyListId, typeId, (nint)Unsafe.AsPointer(in minValue3));
		}
		else if (type == typeof(byte))
		{
			byte localValue = 0;
			H5P.set_fill_value(createPropertyListId, typeId, (nint)Unsafe.AsPointer(in localValue));
		}
		else if (!(type == typeof(string)))
		{
			throw new Exception("Type not supported.");
		}
	}

	public void Dispose()
	{
		H5D.close(DatasetId);
		DataType?.Dispose();
		if (SpaceId != long.MinValue)
		{
			H5S.close(SpaceId);
		}
		if (CreatePropertyListId != long.MinValue)
		{
			H5P.close(CreatePropertyListId);
		}
		if (AccessPropertyListId != long.MinValue)
		{
			H5P.close(AccessPropertyListId);
		}
	}
}
