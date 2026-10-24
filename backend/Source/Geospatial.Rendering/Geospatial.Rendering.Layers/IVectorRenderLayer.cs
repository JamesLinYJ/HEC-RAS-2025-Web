using System.Collections.Generic;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers.Selection;
using Geospatial.Vectors;
using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public interface IVectorRenderLayer : IRenderLayer, IDisplayName, IReadOnlyDisplayName, IBasicSelection
{
	int Count { get; }

	VectorSymbology VectorSymbology { get; set; }

	SelectedSet SelectedSet { get; }

	bool Select(Extent selectExt, SelectMode mode);

	bool Select(Polyline lineIntersection, SelectMode mode);

	bool Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode);

	bool Select(HitTestArgs selectArgs, SelectMode mode);

	IList<object> GetHitTestVectors(HitTestArgs args, out int _hitTestedIdx);

	ISpatialVersion GetSpatialVersionContext();

	long GetSelectionVersion()
	{
		return SelectedSet?.VersionXY ?? 1;
	}
}
