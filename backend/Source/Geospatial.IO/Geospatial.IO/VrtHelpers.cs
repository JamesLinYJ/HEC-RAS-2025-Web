using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.IO;

public class VrtHelpers
{
	public static OperationResult BuildVRTFromSelected(IReadOnlyList<(GdalRasterSummary, int)> bandSelections, out string vrtPath, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		HashSet<GdalRasterSummary> gdalRasterSummaries = new HashSet<GdalRasterSummary>();
		foreach (var bandSelection in bandSelections)
		{
			gdalRasterSummaries.Add(bandSelection.Item1);
		}
		Dictionary<GdalRasterSummary, DatasetIdentifier> datasetIdentifierByGdalRasterSummary = new Dictionary<GdalRasterSummary, DatasetIdentifier>();
		foreach (GdalRasterSummary gdalRasterSummary in gdalRasterSummaries)
		{
			if (gdalRasterSummary.Subdataset != null)
			{
				datasetIdentifierByGdalRasterSummary.Add(gdalRasterSummary, new DatasetIdentifier(gdalRasterSummary.Path, gdalRasterSummary.Driver, gdalRasterSummary.Subdataset));
			}
			else
			{
				datasetIdentifierByGdalRasterSummary.Add(gdalRasterSummary, new DatasetIdentifier(gdalRasterSummary.Path));
			}
		}
		List<Tuple<BandIdentifier, DateTime>> valueTupleList = new List<Tuple<BandIdentifier, DateTime>>();
		foreach (var bandSelection2 in bandSelections)
		{
			GdalRasterSummary gdalRasterSummary2 = bandSelection2.Item1;
			BandIdentifier bandIdentifier = new BandIdentifier(datasetIdentifierByGdalRasterSummary[gdalRasterSummary2], bandSelection2.Item2 + 1);
			valueTupleList.Add(new Tuple<BandIdentifier, DateTime>(bandIdentifier, gdalRasterSummary2.BandTimestamps[bandSelection2.Item2]));
		}
		valueTupleList = valueTupleList.OrderBy((Tuple<BandIdentifier, DateTime> band) => band.Item2).ToList();
		vrtPath = Path.GetTempFileName();
		File.Delete(vrtPath);
		return GDALRaster.BuildMultibandVRT(valueTupleList, vrtPath, null, null, pr);
	}
}
