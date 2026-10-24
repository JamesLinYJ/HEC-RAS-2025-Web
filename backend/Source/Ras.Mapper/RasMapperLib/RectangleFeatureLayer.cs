using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

public class RectangleFeatureLayer : AffineFeatureLayer
{
	public const string RectangleType = "Rectangle";

	public virtual string HDFGroup { get; }

	public virtual string HDFDataset { get; }

	public override bool CanExportAsShapefile { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public new RectangleFeature this[int index] => Rectangle(index);

	public RectangleFeatureLayer()
		: base("Rectangle Layer")
	{
		HDFGroup = "/Rectangles";
		HDFDataset = HDFGroup + "/Rectangles";
		CanExportAsShapefile = false;
		SimpleFeatureTypeName = "Rectangle";
	}

	public RectangleFeatureLayer(string name)
		: base(name)
	{
		HDFGroup = "/Rectangles";
		HDFDataset = HDFGroup + "/Rectangles";
		CanExportAsShapefile = false;
		SimpleFeatureTypeName = "Rectangle";
	}

	public RectangleFeatureLayer(string name, string filename)
		: base(name, filename)
	{
		HDFGroup = "/Rectangles";
		HDFDataset = HDFGroup + "/Rectangles";
		CanExportAsShapefile = false;
		SimpleFeatureTypeName = "Rectangle";
	}

	protected override void LoadFeatureTable()
	{
		if (string.IsNullOrEmpty(SourceFilename) || !SourceFileExists)
		{
			return;
		}
		try
		{
			using H5Reader h5Reader = new H5Reader(SourceFilename);
			if (h5Reader.ExistsAsGroup(HDFGroup))
			{
				HDF5Storage.ReadRectangleFeatures(this, h5Reader, HDFDataset);
				string hdfGroupTerm = HDFGroup + "/Attributes";
				if (h5Reader.ExistsAsDataset(hdfGroupTerm))
				{
					ReadManagedColumns(h5Reader, hdfGroupTerm);
				}
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ErrorMessage = "Could not load layer: " + ex2.Message;
			ProjectData.ClearProjectError();
		}
	}

	protected override bool SaveFeatureTableInternal()
	{
		if (!IsFeatureTableLoaded())
		{
			return false;
		}
		if (string.IsNullOrEmpty(SourceFilename))
		{
			return false;
		}
		if (!SourceFilename.RoughlyEndsWith(".hdf"))
		{
			SourceFilename += ".hdf";
		}
		if (FeatureCount() == 0 && !SourceFileExists)
		{
			return true;
		}
		try
		{
			using H5Writer h5Writer = new H5Writer(SourceFilename);
			HDF5Storage.WriteRectangleFeatures(this, h5Writer, HDFDataset);
			string datasetName = HDFGroup + "/Attributes";
			WriteManagedColumns(h5Writer, datasetName);
			h5Writer.WriteAttribute(HDFGroup, "Type", "Rectangle");
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ErrorMessage = "Could not save layer: " + ex2.Message;
			ProjectData.ClearProjectError();
		}
		return true;
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		return base.GetStaticPlotOptions();
	}

	public IEnumerable<RectangleFeature> Rectangles()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return Rectangle(i);
			}
		}
	}

	public DataRow AddFeature(RectangleFeature rect)
	{
		return AddFeature((AffineFeature)rect);
	}

	public RectangleFeature Rectangle(int fid)
	{
		return (RectangleFeature)Feature(fid);
	}

	public double GetWidth(int fid)
	{
		return Rectangle(fid).Width;
	}

	public double GetHeight(int fid)
	{
		return Rectangle(fid).Height;
	}

	public override List<Type> AvailableFeatureTypes()
	{
		return new Type[1] { typeof(RectangleFeature) }.ToList();
	}
}
