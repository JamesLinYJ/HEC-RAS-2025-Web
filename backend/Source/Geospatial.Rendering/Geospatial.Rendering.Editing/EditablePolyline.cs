using System;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class EditablePolyline : GenericEditableLine<Polyline>
{
	public Polyline Line => Vector;

	public EditablePolyline(Polyline line, IVectorManager vectorManager, Func<EditOperation, bool, bool> executeOperation, SelectedSet selectionCache, OverlayChannels overlayChannels)
		: base(line, vectorManager, VectorType.Line, executeOperation, selectionCache, overlayChannels)
	{
	}
}
