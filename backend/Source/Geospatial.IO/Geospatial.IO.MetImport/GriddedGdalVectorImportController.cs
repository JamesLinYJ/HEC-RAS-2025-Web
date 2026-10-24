using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.GDALAssist;
using Utility.Logging;

namespace Geospatial.IO.MetImport;

public class GriddedGdalVectorImportController : IGriddedDataImportController
{
	public Type ItemType => typeof(GriddedGdalVector);

	public bool CanCombineSources => true;

	public IReadOnlyList<string> GetSubdatasetsOfFiles(IList<string> paths)
	{
		return Array.Empty<string>();
	}

	public IReadOnlyList<IGriddedDataSource> CreateDataSourcesFromFiles(IList<string> paths, string subset)
	{
		using GdalRasterCollection gdrc = GdalRasterCollection.Open(paths);
		List<GdalRasterSummary> gdalRasterSummaries = (from sum in GdalRasterSummary.ParseCollection(gdrc)
			orderby sum.ForecastIssueTimes[0]
			select sum).ToList();
		IReadOnlyList<GriddedGdalVector> vectorPairs = GetVectorPairs(gdalRasterSummaries);
		foreach (GdalRasterSummary gdalRasterSummary in gdalRasterSummaries)
		{
			if (!vectorPairs.Any((GriddedGdalVector pair) => pair.A == gdalRasterSummary || pair.B == gdalRasterSummary))
			{
				throw new ArgumentException(gdalRasterSummary.Path + " does not have a matching pair");
			}
		}
		return vectorPairs;
	}

	public OperationResult CreateSelection(IReadOnlyList<(IGriddedDataSource, int)> selectedBands, out IGriddedDataSelection selection)
	{
		(GdalRasterSummary A, int)[] aBands = selectedBands.Select(((IGriddedDataSource, int) tuple) => (A: (tuple.Item1 as GriddedGdalVector).A, tuple.Item2)).ToArray();
		(GdalRasterSummary, int)[] bBands = selectedBands.Select(((IGriddedDataSource, int) tuple) => (B: (tuple.Item1 as GriddedGdalVector).B, tuple.Item2)).ToArray();
		bool isMagnitudeDirection = false;
		if (selectedBands != null && selectedBands.Count > 0)
		{
			isMagnitudeDirection = (selectedBands[0].Item1 as GriddedGdalVector).IsMagnitudeDirection;
		}
		OperationResult result = GriddedGdalVectorSelection.CreateFromSelections(aBands, bBands, isMagnitudeDirection, out var selection2);
		selection = selection2;
		return result;
	}

	private List<GriddedGdalVector> GetVectorPairs(IList<GdalRasterSummary> collection)
	{
		List<GdalRasterSummary> gdalRasterSummaries = new List<GdalRasterSummary>();
		List<GriddedGdalVector> griddedGdalVectors = new List<GriddedGdalVector>();
		int intervalIndex;
		for (intervalIndex = 0; intervalIndex < collection.Count; intervalIndex++)
		{
			GdalRasterSummary gdalRasterSummary = collection[intervalIndex];
			bool condition = false;
			for (int i = intervalIndex + 1; i < collection.Count; i++)
			{
				GdalRasterSummary gdalRasterSummary2 = collection[i];
				if (gdalRasterSummary.BandTimestamps.SequenceEqual(gdalRasterSummary2.BandTimestamps))
				{
					griddedGdalVectors.Add(new GriddedGdalVector(gdalRasterSummary, gdalRasterSummary2));
					collection.RemoveAt(i);
					condition = true;
					break;
				}
			}
			if (!condition)
			{
				gdalRasterSummaries.Add(gdalRasterSummary);
			}
			collection.RemoveAt(intervalIndex);
			intervalIndex--;
		}
		return griddedGdalVectors;
	}
}
