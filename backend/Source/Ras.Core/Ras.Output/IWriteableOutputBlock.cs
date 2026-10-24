using Ras.Engine;

namespace Ras.Output;

public interface IWriteableOutputBlock
{
	void AddTimestep(MeshTimestep ts);
}
