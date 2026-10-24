using System.Collections;
using System.Collections.Generic;
using Utility.Interfaces;

namespace Geospatial.Rendering.Layers;

public interface IRenderLayerCollection : IRenderLayer, IDisplayName, IReadOnlyDisplayName, IEnumerable<IRenderLayer>, IEnumerable
{
	List<IRenderLayer> Layers { get; }
}
