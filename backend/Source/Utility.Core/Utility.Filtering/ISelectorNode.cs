using System.Collections.Generic;

namespace Utility.Filtering;

public interface ISelectorNode
{
	string Name { get; }

	ISelectorFields Fields { get; }

	IEnumerable<string> MemberNames { get; }

	bool TryGetChildNode(string normalizedName, out ISelectorNode node);

	bool TryGetCollection(string normalizedName, out ISelectorCollection collection);
}
