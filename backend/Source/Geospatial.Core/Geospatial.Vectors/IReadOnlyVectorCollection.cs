using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Geospatial.Vectors;

public interface IReadOnlyVectorCollection<out V> : IEnumerable<V>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<V>, IReadOnlyCollection<V>
{
	ReaderWriterLock Lock { get; }
}
