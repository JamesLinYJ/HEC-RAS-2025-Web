using System.Threading.Tasks;

namespace Utility.Systems;

public interface IImageProvider
{
	string Name { get; }

	string Description { get; }

	Task<byte[]> TryLoadImageAsync();
}
