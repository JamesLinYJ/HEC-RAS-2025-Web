using H5Assist;

namespace Ras.Layers.Terrains;

public interface IH5Parsable<TSelf> where TSelf : IH5Parsable<TSelf>
{
	static abstract bool TryLoad(H5Reader hr, string h5Path, out TSelf instance);

	void Save(H5Writer hw, string h5Path);
}
