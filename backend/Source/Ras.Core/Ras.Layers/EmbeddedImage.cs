using System;
using System.Threading.Tasks;
using H5Assist;
using Utility.Systems;

namespace Ras.Layers;

public class EmbeddedImage : IImageProvider
{
	private EmbeddedImageCollection _parentCollection;

	private byte[] _imageData;

	private int _metadataHashOnLoad;

	public string Name { get; set; }

	public string Description { get; set; }

	public string DateLastModified { get; set; }

	public double X { get; set; }

	public double Y { get; set; }

	public string H5Dataset => _parentCollection?.H5Group + Name;

	public int MetadataHashOnLoad => _metadataHashOnLoad;

	public byte[] CachedImageData => _imageData;

	private EmbeddedImage(string name)
	{
		Name = name;
		_metadataHashOnLoad = ComputeMetadataHash();
	}

	private EmbeddedImage(string name, EmbeddedImageCollection coll)
		: this(name)
	{
		_parentCollection = coll;
	}

	private int ComputeMetadataHash()
	{
		return HashCode.Combine(Name, Description, DateLastModified, X, Y);
	}

	public bool NeedsSave()
	{
		return ComputeMetadataHash() != _metadataHashOnLoad;
	}

	public static EmbeddedImage InMemory(string name, byte[] imgData)
	{
		return new EmbeddedImage(name)
		{
			_imageData = imgData
		};
	}

	public static EmbeddedImage NewImage(string name, byte[] imgData, EmbeddedImageCollection coll)
	{
		return new EmbeddedImage(name, coll)
		{
			_imageData = imgData
		};
	}

	public static EmbeddedImage Open(string name, EmbeddedImageCollection coll)
	{
		EmbeddedImage embeddedImage = new EmbeddedImage(name, coll);
		embeddedImage.LoadMetadata();
		return embeddedImage;
	}

	private void LoadMetadata()
	{
		string ds = H5Dataset;
		_parentCollection.H5Wrapper.Value.UseResource((H5Reader hr) =>
		{
			Description = hr.TryReadStringAttributeScalar(ds, "Description");
			DateLastModified = hr.TryReadStringAttributeScalar(ds, "Date Last Modified");
			if (hr.TryReadAttributeScalar(ds, "X", out double computed))
			{
				X = computed;
			}
			if (hr.TryReadAttributeScalar(ds, "Y", out double computed2))
			{
				Y = computed2;
			}
		});
	}

	public void SaveMetadata(H5Writer hw)
	{
		string h5Dataset = H5Dataset;
		WriteOrDelete(hw, h5Dataset, "Description", Description);
		WriteOrDelete(hw, h5Dataset, "Date Last Modified", DateLastModified);
		hw.WriteAttribute(h5Dataset, "X", X);
		hw.WriteAttribute(h5Dataset, "Y", Y);
	}

	private static void WriteOrDelete(H5Writer hw, string ds, string attrName, string attrValue)
	{
		if (string.IsNullOrEmpty(attrValue))
		{
			hw.DeleteAttribute(ds, attrName);
		}
		else
		{
			hw.WriteAttribute(ds, attrName, attrValue);
		}
	}

	public async Task<byte[]> TryLoadImageAsync()
	{
		if (_imageData != null)
		{
			return _imageData;
		}
		byte[] imageData = await _parentCollection.H5Wrapper.Value.UseResourceAsync((H5Reader hr) =>
		{
			string h5GroupParentCollectionTerm = _parentCollection.H5Group + Name;
			if (!hr.ExistsAsDataset(h5GroupParentCollectionTerm))
			{
				return (byte[])null;
			}
			hr.ReadDataset(h5GroupParentCollectionTerm, out byte[] values);
			return values;
		});
		if (_parentCollection.CacheLoadedImages)
		{
			_imageData = imageData;
		}
		return imageData;
	}
}
