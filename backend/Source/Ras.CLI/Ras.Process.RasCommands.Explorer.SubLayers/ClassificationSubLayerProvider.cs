using System.Collections.Generic;
using Ras.Layers;
using Ras.Layers.Classifiers;

namespace Ras.Process.RasCommands.Explorer.SubLayers;

public static class ClassificationSubLayerProvider
{
	public static IEnumerable<RasTreeNode> GetSubLayers(ClassificationLayer cls, RasTreeNode parent)
	{
		foreach (IClassificationInput layer in cls.Layers)
		{
			string typeName = layer.TypeName;
			string subLayerIds = layer.Name ?? "Unnamed";
			string displaySuffix = "[" + typeName + "]";
			if (layer is RasterClassificationInput rasterClassificationInput)
			{
				bool isValid = rasterClassificationInput.IsValid(out var _);
				int classificationRasterClassificationInputTerm = rasterClassificationInput.Classification?.Count ?? 0;
				displaySuffix = (isValid ? $"[{typeName}, {classificationRasterClassificationInputTerm} classes]" : ("[" + typeName + "] (missing)"));
			}
			else if (layer is PolygonClassificationInput polygonClassificationInput)
			{
				int getClassifierPolygonClassificationInputTerm = polygonClassificationInput.GetClassifier()?.Count ?? 0;
				displaySuffix = $"[{typeName}, {getClassifierPolygonClassificationInputTerm} classes]";
			}
			yield return new RasTreeNode(subLayerIds, parent.FullPath + "/" + subLayerIds, RasNodeType.SubLayer, parent)
			{
				DataSource = layer,
				DisplaySuffix = displaySuffix
			};
		}
	}
}
