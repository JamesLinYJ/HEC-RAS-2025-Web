using System;
using System.IO;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Utility.Logging;
using Visual.Observables;

namespace Visual.Mapping.Observables.Jobs;

public static class ImportBatchJobs
{
	public static async Task<bool> ImportDiscreteTiffRaster<T>(string srcName, string destName, Projection destProj, BatchJob batch = null) where T : unmanaged, IEquatable<T>
	{
		if (batch == null)
		{
			batch = JobHub.Instance.CreateJob("Importing tiff " + Path.GetFileName(srcName));
		}
		Path.GetFileNameWithoutExtension(srcName);
		RasterFileConverterConfig config = RasterFileConverterConfig.Default();
		config.DestinationProjection = destProj;
		OperationResult operationResult = await Task.Run(() => TiffExportEngine.ConvertFile<T>(srcName, destName, config, batch.Reporter));
		batch.MarkComplete(operationResult);
		return operationResult;
	}
}
