using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using H5Assist;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.Units;

namespace Geospatial.IO;

public class H5TimeSeriesRasterDataSource<T> : H5MultiBandRasterProvider<T>, ITimeSeriesRasterProvider<T>, IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, IDiscreteTimesteps where T : unmanaged, IEquatable<T>
{
	private static readonly ILogger _logger = LogHelper.GetLogger("H5TimeSeriesRasterDataSource");

	public const string DefaultTimeAttributeName = "Times";

	public IReadOnlyList<DateTime> Times { get; private set; }

	protected H5TimeSeriesRasterDataSource()
	{
	}

	public static OperationResult TryOpen(NativeResourceWrapper<H5Reader> hwrap, string datasetName, out H5TimeSeriesRasterDataSource<T> res)
	{
		if (hwrap == null || hwrap.IsDisposed)
		{
			throw new ArgumentException("hwrap is null or disposed.");
		}
		return TryInitTsInternal(hwrap, datasetName, out res);
	}

	protected static OperationResult TryInitTsInternal(NativeResourceWrapper<H5Reader> hwrap, string datasetName, out H5TimeSeriesRasterDataSource<T> result)
	{
		result = new H5TimeSeriesRasterDataSource<T>();
		return TryInitTsInternal(result, hwrap, datasetName);
	}

	protected static OperationResult TryInitTsInternal(H5TimeSeriesRasterDataSource<T> h5TimeSeriesRasterDataSource, NativeResourceWrapper<H5Reader> hwrap, string datasetName)
	{
		OperationResult operationResult = H5MultiBandRasterProvider<T>.TryInitBandedInternal(h5TimeSeriesRasterDataSource, hwrap, datasetName);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		return hwrap.UseResource((H5Reader hr) =>
		{
			string fn = hr.Filename;
			return h5TimeSeriesRasterDataSource._hzDset.UseResource(hr, (H5DatasetID dsetId) =>
			{
				if (!hr.ExistsAsAttribute(datasetName, "Times"))
				{
					return OperationResult.Fail(H5Reader.MessageCodes.GetAttributeNotFound(fn, datasetName, "Times", "long"));
				}
				Type attributeType = hr.GetAttributeType(datasetName, "Times");
				int arrayRank = attributeType.GetArrayRank();
				if (arrayRank != 1)
				{
					return OperationResult.Fail(H5Reader.MessageCodes.GetDatasetIncorrectRank(fn, datasetName, 1, arrayRank));
				}
				Type elementType = attributeType.GetElementType();
				if (elementType == typeof(string))
				{
					hr.ReadAttribute(datasetName, "Times", out string[] values);
					h5TimeSeriesRasterDataSource.Times = Time.TryParseFormattedDateTimes(values).AsReadOnly();
					_logger.LogWarning("Time data written as string array - move to longs.");
				}
				else
				{
					if (!(elementType == typeof(long)))
					{
						return OperationResult.Fail(H5Reader.MessageCodes.GetAttributeIncorrectType(fn, datasetName, "Times", "long", elementType.Name.ToString()));
					}
					hr.ReadAttribute(datasetName, "Times", out long[] values2);
					h5TimeSeriesRasterDataSource.Times = values2.Select((long t) => new DateTime(t)).ToArray();
				}
				return OperationResult.Success();
			});
		});
	}
}
