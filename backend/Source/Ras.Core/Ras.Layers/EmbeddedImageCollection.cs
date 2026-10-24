using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Logging;
using Utility.Systems;

namespace Ras.Layers;

public class EmbeddedImageCollection : IImageCollectionProvider<EmbeddedImage>, IImageCollectionProviderBase, INotifyCollectionChanged
{
	private static ILogger _logger = LogHelper.GetLogger("EmbeddedImageCollection");

	private List<EmbeddedImage> _images;

	private int _threadCount;

	private int _imageMetadataHash;

	public Any<NativeResourceWrapper<H5Reader>> H5Wrapper { get; }

	public string H5Group { get; }

	public List<EmbeddedImage> Images => _images ?? LoadImageData();

	public bool HasLoaded => _images != null;

	public bool CacheLoadedImages { get; set; } = true;

	public event NotifyCollectionChangedEventHandler CollectionChanged;

	public static EmbeddedImageCollection InMemory()
	{
		return new EmbeddedImageCollection
		{
			_images = new List<EmbeddedImage>()
		};
	}

	private EmbeddedImageCollection()
	{
	}

	public EmbeddedImageCollection(string rootFolder, Any<NativeResourceWrapper<H5Reader>> h5Wrapper)
	{
		H5Group = rootFolder;
		if (!H5Reader.PathSeparatorChars.Any((char c) => H5Group.EndsWith(c)))
		{
			H5Group += "/";
		}
		H5Wrapper = h5Wrapper;
	}

	public int PeekCount()
	{
		return _images?.Count ?? H5Wrapper.Value.UseResource((H5Reader hr) => hr.GetDatasetCount(H5Group));
	}

	public EmbeddedImage Add(string name, string desc, byte[] imgData)
	{
		EmbeddedImage embeddedImage = EmbeddedImage.NewImage(name, imgData, this);
		embeddedImage.Description = desc;
		Images.Add(embeddedImage);
		CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, embeddedImage, Images.Count - 1));
		return embeddedImage;
	}

	public EmbeddedImage AddFromFile(string filename)
	{
		byte[] imgData = File.ReadAllBytes(filename);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
		EmbeddedImage embeddedImage = Add(fileNameWithoutExtension, "Imported from " + filename, imgData);
		FileInfo fileInfo = new FileInfo(filename);
		embeddedImage.DateLastModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
		return embeddedImage;
	}

	public async Task<EmbeddedImage> AddFromFileAsync(string filename)
	{
		byte[] imgData = await File.ReadAllBytesAsync(filename);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
		EmbeddedImage embeddedImage = Add(fileNameWithoutExtension, "Imported from " + filename, imgData);
		FileInfo fileInfo = new FileInfo(filename);
		embeddedImage.DateLastModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
		return embeddedImage;
	}

	public bool NeedsSave()
	{
		if (!HasLoaded)
		{
			return false;
		}
		return _imageMetadataHash != ComputeMetadataHash();
	}

	public async Task<List<EmbeddedImage>> LoadImageDataAsync()
	{
		if (_images != null)
		{
			return _images;
		}
		if (Interlocked.Increment(ref _threadCount) != 1)
		{
			_logger.LogWarning("LoadImageDataAsync is not fully threadsafe yet - don't invoke from multiple threads.");
		}
		_images = await H5Wrapper.Value.UseResourceAsync((H5Reader hr) => LoadImageDataInternal(hr));
		_imageMetadataHash = ComputeMetadataHash();
		return _images;
	}

	public List<EmbeddedImage> LoadImageData()
	{
		if (_images != null)
		{
			return _images;
		}
		_images = H5Wrapper.Value.UseResource((H5Reader hr) => LoadImageDataInternal(hr));
		_imageMetadataHash = ComputeMetadataHash();
		return _images;
	}

	private List<EmbeddedImage> LoadImageDataInternal(H5Reader hr)
	{
		List<EmbeddedImage> embeddedImages = new List<EmbeddedImage>();
		if (!hr.ExistsAsGroup(H5Group))
		{
			return embeddedImages;
		}
		foreach (string datasetName in hr.GetDatasetNames(H5Group))
		{
			embeddedImages.Add(EmbeddedImage.Open(datasetName, this));
		}
		return embeddedImages;
	}

	private int ComputeMetadataHash()
	{
		if (!HasLoaded)
		{
			return 0;
		}
		int combine = 0;
		foreach (EmbeddedImage image in _images)
		{
			combine = HashCode.Combine(combine, image.MetadataHashOnLoad);
		}
		return combine;
	}

	public void Save(H5Writer hw)
	{
		if (!HasLoaded)
		{
			return;
		}
		foreach (EmbeddedImage image in Images)
		{
			string h5Dataset = image.H5Dataset;
			if (!hw.ExistsAsDataset(h5Dataset))
			{
				if (image.CachedImageData == null)
				{
					throw new Exception("No image data found.");
				}
				hw.WriteDataset(h5Dataset, image.CachedImageData);
			}
			image.SaveMetadata(hw);
		}
	}

	public async Task<IReadOnlyList<IImageProvider>> LoadGenericImageDataAsync()
	{
		return await LoadImageDataAsync();
	}
}
