using System.Collections.Generic;

namespace H5Assist.Differences;

public class PatchCreationLog
{
	public List<ExtractedDataset> Extracted { get; } = new List<ExtractedDataset>();

	public int DatasetsKeptInPatch { get; set; }

	public int NewBaseDatasets { get; set; }

	public int DeduplicatedDatasets { get; set; }

	public long TotalExtractedBytes { get; set; }

	public bool HasChanges => Extracted.Count > 0;
}
