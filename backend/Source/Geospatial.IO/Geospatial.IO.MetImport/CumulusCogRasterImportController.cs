using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Utility.Logging;

namespace Geospatial.IO.MetImport;

public class CumulusCogRasterImportController : IGriddedDataImportController
{
	public Type ItemType => typeof(CumulusCogRaster);

	public bool CanCombineSources => false;

	public Extent ClipExtent { get; set; } = Extent.Infinite;

	public Projection ClipProjection { get; set; }

	public IReadOnlyList<string> GetSubdatasetsOfFiles(IList<string> paths)
	{
		return Array.Empty<string>();
	}

	public IReadOnlyList<IGriddedDataSource> CreateDataSourcesFromFiles(IList<string> paths, string subset)
	{
		return Array.Empty<IGriddedDataSource>();
	}

	public OperationResult CreateSelection(IReadOnlyList<(IGriddedDataSource, int)> selectedBands, out IGriddedDataSelection selection)
	{
		OperationResult result = CumulusCogRasterSelection.CreateFromSources((from s in selectedBands
			select s.Item1 as CumulusCogRaster into s
			where s != null
			select s).Distinct().ToArray(), ClipExtent, ClipProjection, out var selection2);
		selection = selection2;
		return result;
	}
}
