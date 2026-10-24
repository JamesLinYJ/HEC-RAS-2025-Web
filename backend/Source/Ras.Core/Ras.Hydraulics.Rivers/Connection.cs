using System.Diagnostics;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.Rivers;

[DebuggerDisplay("{Type}: {Name}")]
public class Connection : IDataHash
{
	[HashContributor]
	[Metadata("Type")]
	public ConnectionType Type { get; set; }

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; private set; }

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
