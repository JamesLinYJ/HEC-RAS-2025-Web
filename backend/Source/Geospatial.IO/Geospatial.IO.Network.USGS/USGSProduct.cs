using System.Linq;
using System.Text;
using Utility;

namespace Geospatial.IO.Network.USGS;

public class USGSProduct
{
	private USGSItem _item;

	private USGSDataType _dataType;

	public string CellSize { get; set; }

	public string Description { get; set; }

	public string Date { get; set; }

	public string FileSize { get; set; }

	public USGSProduct(USGSItem usgsItem)
	{
		_item = usgsItem;
		Description = usgsItem.Title;
		Date = usgsItem.PublicationDate;
		if (usgsItem.SizeInBytes == 0L)
		{
			FileSize = "Unknown";
		}
		else
		{
			FileSize = StringHelpers.FormatBytes(usgsItem.SizeInBytes);
		}
		string toLowerInvariant = Description.ToLowerInvariant();
		if (toLowerInvariant.Contains("1 arc second") || toLowerInvariant.Contains("1 arc-second"))
		{
			CellSize = "30m";
			_dataType = USGSDataType.ElevationModel;
		}
		else if (toLowerInvariant.Contains("1 meter") || toLowerInvariant.Contains("one meter"))
		{
			CellSize = "1m";
			_dataType = USGSDataType.ElevationModel;
		}
		else if (toLowerInvariant.Contains("1/3 arc second") || toLowerInvariant.Contains("1/3 arc-second") || toLowerInvariant.Contains("13 arc second") || toLowerInvariant.Contains("13 arc-second"))
		{
			CellSize = "10m";
			_dataType = USGSDataType.ElevationModel;
			if (toLowerInvariant.Contains("13 arc second") || toLowerInvariant.Contains("13 arc-second"))
			{
				usgsItem.SizeInBytes *= 1000L;
				if (usgsItem.SizeInBytes == 0L)
				{
					FileSize = "Unknown";
				}
				else
				{
					FileSize = StringHelpers.FormatBytes(usgsItem.SizeInBytes);
				}
			}
		}
		else if (toLowerInvariant.Contains("-scale"))
		{
			int indexOf = toLowerInvariant.IndexOf("-scale");
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = indexOf - 1; i >= 0; i += -1)
			{
				if (char.IsWhiteSpace(toLowerInvariant[i]))
				{
					string cellSize = new string(stringBuilder.ToString().Reverse().ToArray()) + "-scale";
					CellSize = cellSize;
					_dataType = USGSDataType.TopoMap;
				}
				stringBuilder.Append(toLowerInvariant[i]);
			}
		}
		else if (toLowerInvariant.Contains("1/9 arc second") || toLowerInvariant.Contains("1/9 arc-second"))
		{
			CellSize = "3m";
			_dataType = USGSDataType.ElevationModel;
		}
		else if (toLowerInvariant.Contains("5 meter") || toLowerInvariant.Contains("five meter"))
		{
			CellSize = "5m";
			_dataType = USGSDataType.ElevationModel;
		}
		else if (toLowerInvariant.Contains("2 arc second") || toLowerInvariant.Contains("2 arc-second"))
		{
			CellSize = "60m";
			_dataType = USGSDataType.ElevationModel;
		}
		else if (toLowerInvariant.Contains("original"))
		{
			CellSize = "Original";
			_dataType = USGSDataType.ElevationModel;
		}
	}

	private static bool ItemBoundingBoxValid(USGSItem usgsItem)
	{
		if (usgsItem.BoundingBox == null)
		{
			return false;
		}
		if (usgsItem.BoundingBox.MinX == 0.0 && usgsItem.BoundingBox.MaxX == 0.0 && usgsItem.BoundingBox.MinY == 0.0 && usgsItem.BoundingBox.MaxY == 0.0)
		{
			return false;
		}
		return true;
	}

	public long GetFileSizeBytes()
	{
		return _item.SizeInBytes;
	}

	public string GetMetaDataURL()
	{
		return _item.MetaURL;
	}

	public string GetProductDownloadURL()
	{
		return _item.DownloadURL;
	}

	public USGSDataType GetDataType()
	{
		return _dataType;
	}
}
