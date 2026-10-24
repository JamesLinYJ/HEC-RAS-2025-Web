using System.IO;

namespace Utility.Systems;

public interface IStreamableObject
{
	void StreamIn(BinaryReader br);

	void StreamOut(BinaryWriter bw);

	static T StreamInFromFile<T>(string filename) where T : IStreamableObject, new()
	{
		T localValue = new T();
		localValue.StreamInFromFile(filename);
		return localValue;
	}
}
