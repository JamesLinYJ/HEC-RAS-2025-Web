using Geospatial.Vectors;
using Utility.Progress;

namespace Geospatial;

public interface IBlame
{
	void Blame(Point pt, ProgressReporter reporter);
}
