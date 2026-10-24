using Utility.Interfaces;

namespace Visual.Mapping.Observables;

public interface IMapLayerVisualContext : IFilename
{
	string XmlNodeIdentifier { get; }

	bool IFilename.IsInMemory => false;
}
