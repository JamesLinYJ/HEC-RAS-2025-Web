using System.Collections.Generic;
using Utility.VisualElements;

namespace Geospatial.Rendering.Systems.Annotations;

public interface IMapToolTip : IVisualElement, ICanvasPositionable
{
	void SetVisibility(bool vis);

	void SetToolTips(IEnumerable<LayerToolTip> toolTips);
}
