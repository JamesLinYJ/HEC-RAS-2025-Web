using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;

namespace Utility.Systems;

public interface IImageCollectionProvider<T> : IImageCollectionProviderBase, INotifyCollectionChanged where T : IImageProvider
{
	Task<List<T>> LoadImageDataAsync();

	new T Add(string name, string desc, byte[] imgData);

	new T AddFromFile(string filename);

	new Task<T> AddFromFileAsync(string filename);

	IImageProvider IImageCollectionProviderBase.Add(string name, string desc, byte[] imgData)
	{
		return Add(name, desc, imgData);
	}

	IImageProvider IImageCollectionProviderBase.AddFromFile(string filename)
	{
		return AddFromFile(filename);
	}

	async Task<IImageProvider> IImageCollectionProviderBase.AddFromFileAsync(string filename)
	{
		return await AddFromFileAsync(filename);
	}
}
