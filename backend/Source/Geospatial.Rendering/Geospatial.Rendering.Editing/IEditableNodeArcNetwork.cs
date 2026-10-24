using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public interface IEditableNodeArcNetwork
{
	OverlayChannels GlobalOverlay { get; }

	bool TryPreviewSelectArc(HitTestArgs args);

	bool TryPreviewSelectNode(HitTestArgs args);

	bool TryPreviewSplitArcs(HitTestArgs args);

	bool TryPreviewSplitArcsAtVertex(HitTestArgs args);

	void PreviewAddArc(HitTestArgs args);

	bool TryPreviewMoveSelectedNodes(HitTestArgs hit, Vector delta, IEnumerable<int> nodeIndices);
}
