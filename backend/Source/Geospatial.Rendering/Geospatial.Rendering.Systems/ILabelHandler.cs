using System.Collections.Generic;

namespace Geospatial.Rendering.Systems;

public interface ILabelHandler
{
	void AddLabel(ILabelDefinition label);

	void AddLabels(IList<ILabelDefinition> labels);

	void RemoveLabel(ILabelDefinition label);

	void RemoveLabels(IList<ILabelDefinition> labels);
}
