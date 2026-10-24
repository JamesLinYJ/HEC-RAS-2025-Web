using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;

namespace Utility.Systems;

public interface IImageCollectionProviderBase : INotifyCollectionChanged
{
	Task<IReadOnlyList<IImageProvider>> LoadGenericImageDataAsync();

	IImageProvider Add(string name, string desc, byte[] imgData);

	IImageProvider AddFromFile(string filename);

	Task<IImageProvider> AddFromFileAsync(string filename);
}
