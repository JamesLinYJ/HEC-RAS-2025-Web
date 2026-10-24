using System.Collections.Generic;

namespace Geospatial.Rendering.Layers.Selection;

public interface IBasicSelection
{
	IList<object> GetSelectedVectors();
}
