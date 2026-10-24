using System.Collections.Generic;
using System.Xml;
using Geospatial.GDALAssist;

namespace RasMapperLib;

public class MultiDatasetInterpolatedLayer : Layer
{
	private List<MultiBandInterpolatedLayer> _subdatasets;

	public MultiDatasetInterpolatedLayer()
		: base("Complex Dataset")
	{
	}

	public MultiDatasetInterpolatedLayer(string lyrName, string filename)
		: base(lyrName, filename)
	{
		EnsureDataLoaded();
	}

	protected override void XMLLoadMetadata(XmlElement xmlNode)
	{
		base.XMLLoadMetadata(xmlNode);
	}

	protected override void XMLLoadChildren(XmlElement xmlEl)
	{
		base.XMLLoadChildren(xmlEl);
	}

	public void EnsureDataLoaded()
	{
		if (_subdatasets == null)
		{
			LoadData();
		}
	}

	private void LoadData()
	{
		_subdatasets = new List<MultiBandInterpolatedLayer>();
		Nodes.Clear();
		if (!SourceFileExists)
		{
			return;
		}
		using GDALRaster gDALRaster = new GDALRaster(SourceFilename);
		Dictionary<string, string> subDatasets = gDALRaster.GetSubDatasets();
		foreach (KeyValuePair<string, string> subDataset in subDatasets)
		{
			MultiBandInterpolatedLayer multiBandInterpolatedLayer = new MultiBandInterpolatedLayer(subDataset.Value, SourceFilename, subDataset.Key);
			_subdatasets.Add(multiBandInterpolatedLayer);
			Nodes.Add(multiBandInterpolatedLayer);
		}
	}
}
