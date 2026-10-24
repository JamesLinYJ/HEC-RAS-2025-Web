using System;
using System.Collections.Generic;

namespace Ras.Process.RasterCommands.Explorer;

public interface IRasterMetadataSource : IDisposable
{
	string DriverLabel { get; }

	string DisplayName { get; }

	bool CanEditMetadata { get; }

	bool CanEditGeoreference { get; }

	bool SupportsTilePreview { get; }

	RasterSummary GetSummary();

	IReadOnlyList<SubdatasetEntry> GetSubdatasets();

	GeoreferenceInfo GetGeoreference();

	ProjectionInfo GetProjection();

	IReadOnlyList<MetadataEntry> GetMetadata();

	IReadOnlyList<PyramidLevelInfo> GetPyramidLevels();

	bool TryGetTilePreview(out TilePreview preview, out string error);

	bool TrySetMetadataItem(string key, string stringValue, out string error);

	bool TrySetGeoreference(double originX, double originY, double cellSize, out string error);

	bool TrySetTiffTag(int levelIndex, int tagId, string stringValue, out string error);

	bool TryOpenSubdataset(SubdatasetEntry entry, out IRasterMetadataSource sub, out string error);
}
