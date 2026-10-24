using System;
using HDF.PInvoke;
using Microsoft.Extensions.Logging;

namespace H5Assist.Identifiers;

public class H5AttributeID : IDisposable
{
	private const long NULLID = long.MinValue;

	private static readonly ILogger _logger = H5Reader.Logger;

	public long AttributeId = long.MinValue;

	public H5DataTypeID DataType;

	public long SpaceId = long.MinValue;

	public string ObjectName;

	public string AttributeName;

	public void GetNativeIDs(out long attrID, out long datatypeID, out long dataspaceID)
	{
		attrID = AttributeId;
		datatypeID = DataType.TypeId;
		dataspaceID = SpaceId;
	}

	public static H5AttributeID Open(long fileId, string objectName, string attributeName, bool needType, bool needSpace)
	{
		H5AttributeID h5AttributeID = new H5AttributeID
		{
			ObjectName = objectName,
			AttributeName = attributeName,
			AttributeId = H5A.open_by_name(fileId, objectName, attributeName, 0L, 0L)
		};
		if (h5AttributeID.AttributeId < 0)
		{
			_logger.LogError("{Attribute} was not opened successfully on {Object}. {ErrorCode}", h5AttributeID.AttributeName, h5AttributeID.ObjectName, h5AttributeID.AttributeId);
			throw new Exception("Error opening attribute: " + objectName + ": " + attributeName);
		}
		if (needType)
		{
			h5AttributeID.DataType = H5DataTypeID.Open(h5AttributeID);
		}
		if (needSpace)
		{
			h5AttributeID.SpaceId = H5A.get_space(h5AttributeID.AttributeId);
		}
		return h5AttributeID;
	}

	public static H5AttributeID Open(long objectId, string attributeName, bool needType, bool needSpace)
	{
		H5AttributeID h5AttributeID = new H5AttributeID
		{
			ObjectName = "",
			AttributeName = attributeName,
			AttributeId = H5A.open_by_name(objectId, H5Reader.CURRENT_LOCATION, attributeName, 0L, 0L)
		};
		if (needType)
		{
			h5AttributeID.DataType = H5DataTypeID.Open(h5AttributeID);
		}
		if (needSpace)
		{
			h5AttributeID.SpaceId = H5A.get_space(h5AttributeID.AttributeId);
		}
		return h5AttributeID;
	}

	public static H5AttributeID Create(long objectId, string attributeName, Type type, ulong[] size, int setSize = 0)
	{
		H5AttributeID h5AttributeID = new H5AttributeID
		{
			ObjectName = "",
			AttributeName = attributeName,
			DataType = new H5DataTypeID(type, setSize),
			SpaceId = H5S.create_simple(size.Length, size, null)
		};
		h5AttributeID.AttributeId = H5A.create(objectId, attributeName, h5AttributeID.DataType.TypeId, h5AttributeID.SpaceId, 0L, 0L);
		if (h5AttributeID.AttributeId == -1)
		{
			throw new Exception("Could not initialize attribute " + attributeName + ". Dataset may not exist, or attribute may be too large. (Note that HDF5 version 1.6 could not create large attributes.)");
		}
		return h5AttributeID;
	}

	public static H5AttributeID CreateScalar(long objectId, string attributeName, Type type, int setSize = 0)
	{
		H5AttributeID h5AttributeID = new H5AttributeID
		{
			ObjectName = "",
			AttributeName = attributeName,
			DataType = new H5DataTypeID(type, setSize),
			SpaceId = H5S.create(H5S.class_t.SCALAR)
		};
		if (type == typeof(string)) H5T.set_cset(h5AttributeID.DataType.TypeId, H5T.cset_t.UTF8);
		h5AttributeID.AttributeId = H5A.create(objectId, attributeName, h5AttributeID.DataType.TypeId, h5AttributeID.SpaceId, 0L, 0L);
		return h5AttributeID;
	}

	public void Dispose()
	{
		H5A.close(AttributeId);
		DataType?.Dispose();
		if (SpaceId != long.MinValue)
		{
			H5S.close(SpaceId);
		}
	}
}
