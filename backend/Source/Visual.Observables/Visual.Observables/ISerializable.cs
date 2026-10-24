using System.Xml.Linq;

namespace Visual.Observables;

public interface ISerializable
{
	void Serialize(XElement serializeTo);

	void Deserialize(XElement element);
}
