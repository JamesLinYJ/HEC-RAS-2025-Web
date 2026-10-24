using System;
using HDF.PInvoke;
using Microsoft.Extensions.Logging;

namespace H5Assist.Identifiers;

internal class H5ObjectID : IDisposable
{
	private static readonly ILogger _logger = H5Reader.Logger;

	public long ObjectId;

	public string ObjectName;

	public H5ObjectID(long fileId, string objectName)
	{
		ObjectName = objectName;
		ObjectId = H5O.open(fileId, objectName, 0L);
		if (ObjectId < 0)
		{
			_logger.LogError("{Object} was not opened successfully. {ErrorCode}", objectName, ObjectId);
			throw new Exception("Error opening object: " + objectName);
		}
	}

	public void Dispose()
	{
		H5O.close(ObjectId);
	}
}
