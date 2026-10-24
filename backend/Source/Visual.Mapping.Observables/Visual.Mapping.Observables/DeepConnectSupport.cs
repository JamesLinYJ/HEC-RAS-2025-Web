using System.Collections.Generic;
using Geospatial.Rendering;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Visual.Mapping.Observables.Map;

namespace Visual.Mapping.Observables;

public static class DeepConnectSupport
{
	public static SnapSelector.CandidateSupplier MakeSupplier(MapBase map, ILineSegmentSource excludeSelf)
	{
		if (map == null)
		{
			return null;
		}
		return (HitTestArgs hit, double toleranceWorld) =>
		{
			Extent extent = Extent.CenteredOn(hit.QueryPoint, toleranceWorld * 2.0);
			List<SnapSelector.CandidateSlice> candidateSlices = new List<SnapSelector.CandidateSlice>(8);
			foreach (IRenderLayer treeEnabledLayer in map.TreeEnabledLayers)
			{
				if (treeEnabledLayer is ILineSourceLayer lineSourceLayer && lineSourceLayer.TryGetLineSource(out var source) && source != excludeSelf)
				{
					IList<FeatureSegmentSubset> elements = source.GetSegmentSpatialIndex().GetElements(extent);
					if (elements.Count > 0)
					{
						candidateSlices.Add(new SnapSelector.CandidateSlice(elements, source.GetLine, treeEnabledLayer));
					}
				}
			}
			return candidateSlices;
		};
	}
}
