using System;
using System.Collections.Generic;
using Utility.Logging;

namespace Geospatial.IO.MetImport;

public interface IGriddedDataImportController
{
	Type ItemType { get; }

	bool CanCombineSources { get; }

	OperationResult CreateSelection(IReadOnlyList<(IGriddedDataSource, int)> selectedBands, out IGriddedDataSelection selection);

	IReadOnlyList<IGriddedDataSource> CreateDataSourcesFromFiles(IList<string> paths, string subset);

	IReadOnlyList<string> GetSubdatasetsOfFiles(IList<string> paths);
}
