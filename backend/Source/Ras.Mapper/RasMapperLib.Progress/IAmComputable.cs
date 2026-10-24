using System.Runtime.InteropServices;
using Utility.Progress;

namespace RasMapperLib.Progress;

[ComVisible(false)]
public interface IAmComputable
{
	void Initialize(IDisplayProgress cw);

	void Run(ProgressReporter reporter);

	void Complete();
}
