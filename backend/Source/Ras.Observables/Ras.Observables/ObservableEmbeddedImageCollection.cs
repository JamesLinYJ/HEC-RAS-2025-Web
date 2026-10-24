using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers;
using Utility.DataStructures;

namespace Ras.Observables;

internal class ObservableEmbeddedImageCollection : ObservableObject
{
	private EmbeddedImageCollection _eic;

	private AdvancedCollection<EmbeddedImage> _images;

	public AdvancedCollection<EmbeddedImage> Images
	{
		get
		{
			if (_images == null)
			{
				SetProperty(ref _images, new AdvancedCollection<EmbeddedImage>(_eic.Images), "Images");
				_images.CollectionChanged += ImagesCollectionChanged;
			}
			return _images;
		}
	}

	private void ImagesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			_eic.Images.Clear();
			_eic.Images.AddRange(Images);
			return;
		}
		if (e.NewItems != null)
		{
			foreach (EmbeddedImage newItem in e.NewItems)
			{
				_eic.Images.Add(newItem);
			}
		}
		if (e.OldItems == null)
		{
			return;
		}
		foreach (EmbeddedImage oldItem in e.OldItems)
		{
			_eic.Images.Remove(oldItem);
		}
	}

	public ObservableEmbeddedImageCollection(EmbeddedImageCollection collection)
	{
		_eic = collection;
	}
}
