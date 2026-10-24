using System.Diagnostics;
using Geospatial;
using Geospatial.Vectors;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.Classic;

[DebuggerDisplay("{Name}")]
public class CulvertGroup : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	public long VersionXY => 1L;

	public Extent Extent => Extent.NullExtent;

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	[HashContributor]
	[Metadata("Shape")]
	public int ShapeID { get; set; }

	[HashContributor]
	[Metadata("Chart")]
	public int ChartID { get; set; }

	[HashContributor]
	[Metadata("Scale")]
	public int ScaleID { get; set; }

	[HashContributor]
	[Metadata("Rise")]
	public float Rise { get; set; }

	[HashContributor]
	[Metadata("Span")]
	public float Span { get; set; }

	[HashContributor]
	[Metadata("Length")]
	public float Length { get; set; }

	[HashContributor]
	[Metadata("US Distance")]
	public float USDistance { get; set; }

	[HashContributor]
	[Metadata("Top Mann")]
	public float TopMann { get; set; }

	[HashContributor]
	[Metadata("Bot Mann")]
	public float BotMann { get; set; }

	[HashContributor]
	[Metadata("Depth Bot Mann")]
	public float DepthBottomMann { get; set; }

	[HashContributor]
	[Metadata("Depth Blck")]
	public float DepthBlck { get; set; }

	[HashContributor]
	[Metadata("Entr Loss")]
	public float EntranceLoss { get; set; }

	[HashContributor]
	[Metadata("Exit Loss")]
	public float ExitLoss { get; set; }

	[HashContributor]
	[Metadata("Invert US")]
	public float UpstreamInvert { get; set; }

	[HashContributor]
	[Metadata("Invert DS")]
	public float DownstreamInvert { get; set; }

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
