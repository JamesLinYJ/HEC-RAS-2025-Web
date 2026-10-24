using System.Collections.Generic;

namespace Ras.Process.H5Commands.Explorer;

public class NodeInfoData
{
	public Dictionary<string, object> Attributes { get; set; }

	public DatasetProperties DatasetInfo { get; set; }
}
