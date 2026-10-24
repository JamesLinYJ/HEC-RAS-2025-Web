using System.Collections.Generic;

namespace Utility.Metadata;

public interface IHasMetadata
{
	public delegate void MetadataUpdatedEvent(IHasMetadata sender);

	event MetadataUpdatedEvent MetadataUpdated;

	List<IMetadataBase> GetMetadata();
}
