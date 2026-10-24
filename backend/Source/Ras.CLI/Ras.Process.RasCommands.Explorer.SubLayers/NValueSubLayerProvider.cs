using System.Collections.Generic;
using Ras.Layers;

namespace Ras.Process.RasCommands.Explorer.SubLayers;

public static class NValueSubLayerProvider
{
	public static IEnumerable<RasTreeNode> GetSubLayers(NValueLayer nv, RasTreeNode parent)
	{
		nv.Mode.ToString();
		if (nv.Mode == NValueSource.Constant)
		{
			yield return new RasTreeNode("Mode", parent.FullPath + "/Mode", RasNodeType.SubLayer, parent)
			{
				DataSource = nv,
				DisplaySuffix = $"[Constant: {nv.ConstantValue:F3}]"
			};
		}
		else if (nv.Mode == NValueSource.ClassificationLayer)
		{
			string classificationLayerName = nv.ClassificationLayerName ?? "(none)";
			yield return new RasTreeNode("Mode", parent.FullPath + "/Mode", RasNodeType.SubLayer, parent)
			{
				DataSource = nv,
				DisplaySuffix = "[Classification: " + classificationLayerName + "]"
			};
			NVTable interpretationTable = nv.InterpretationTable;
			if (interpretationTable != null && interpretationTable.Count > 0)
			{
				yield return new RasTreeNode("InterpretationTable", parent.FullPath + "/InterpretationTable", RasNodeType.SubLayer, parent)
				{
					DataSource = nv.InterpretationTable,
					DisplaySuffix = $"[{nv.InterpretationTable.Count} entries]"
				};
			}
		}
		else if (nv.Mode == NValueSource.ImportedRaster)
		{
			yield return new RasTreeNode("Mode", parent.FullPath + "/Mode", RasNodeType.SubLayer, parent)
			{
				DataSource = nv,
				DisplaySuffix = "[Imported Raster]"
			};
		}
	}
}
