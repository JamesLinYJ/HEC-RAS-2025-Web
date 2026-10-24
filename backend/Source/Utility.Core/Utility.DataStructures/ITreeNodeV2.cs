using System.Collections.Generic;

namespace Utility.DataStructures;

public interface ITreeNodeV2<T> where T : ITreeNodeV2<T>
{
	IList<T> Children { get; }
}
