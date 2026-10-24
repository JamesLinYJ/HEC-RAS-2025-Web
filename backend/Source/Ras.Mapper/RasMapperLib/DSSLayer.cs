using System.IO;
using System.Xml;
using Hec.Dss;

namespace RasMapperLib;

public class DSSLayer : Layer
{
	public static RecordType[] TimeSeriesRecordTypes = new RecordType[2]
	{
		RecordType.IrregularTimeSeries,
		RecordType.RegularTimeSeries
	};

	public static RecordType[] PairedDataRecordTypes = new RecordType[1] { RecordType.PairedData };

	public static RecordType[] GridRecordTypes = new RecordType[1] { RecordType.Grid };

	private bool _hasLoaded;

	public DSSRasterLayer RasterLayer;

	public DSSPointLayer PointLayer;

	public DSSLayer()
	{
		_hasLoaded = false;
	}

	public DSSLayer(string srcFilename)
		: base(Path.GetFileNameWithoutExtension(srcFilename), srcFilename)
	{
		_hasLoaded = false;
		EnsureDataLoaded();
	}

	private void EnsureDataLoaded()
	{
		if (!_hasLoaded)
		{
			RasterLayer = new DSSRasterLayer("Rasters", SourceFilename);
			PointLayer = new DSSPointLayer("Time Series", SourceFilename);
			Nodes.Add(RasterLayer);
			Nodes.Add(PointLayer);
			_hasLoaded = true;
		}
	}

	protected override void XMLLoadMetadata(XmlElement xmlNode)
	{
		base.XMLLoadMetadata(xmlNode);
		EnsureDataLoaded();
	}

	protected override bool XMLLoadChild(XmlElement xmlEl)
	{
		EnsureDataLoaded();
		LoadXmlIntoExistingNodes(xmlEl, recurse: false);
		return true;
	}
}
