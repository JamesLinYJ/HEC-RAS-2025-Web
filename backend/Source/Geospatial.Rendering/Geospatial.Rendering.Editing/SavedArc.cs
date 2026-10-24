using System.Diagnostics;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

[DebuggerDisplay("{ToString(),nq}")]
public struct SavedArc
{
	public int FID;

	public Arc OriginalArc;

	public object[] MetaDataRow;

	public static SavedArc NullSavedArc => new SavedArc(-1, null, null);

	public SavedArc(int fid, Arc originalArc, object[] metaDataRow)
	{
		FID = fid;
		OriginalArc = originalArc;
		MetaDataRow = metaDataRow;
	}

	public bool IsNull()
	{
		return FID < 0;
	}

	public override string ToString()
	{
		return "FID: " + FID + ", ARC: " + OriginalArc.ToString();
	}
}
