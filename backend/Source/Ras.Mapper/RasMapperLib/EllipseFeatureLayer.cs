using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

public class EllipseFeatureLayer : AffineFeatureLayer
{
	public const string CircleType = "Circle";

	public override string SimpleFeatureTypeName { get; set; }

	public virtual string HDFGroup { get; }

	public override bool CanExportAsShapefile { get; }

	public new Ellipse this[int index] => Ellipse(index);

	public EllipseFeatureLayer()
		: base("Ellipse Layer")
	{
		SimpleFeatureTypeName = "Ellipse";
		HDFGroup = "/Circles";
		CanExportAsShapefile = false;
	}

	public EllipseFeatureLayer(string name)
		: base(name)
	{
		SimpleFeatureTypeName = "Ellipse";
		HDFGroup = "/Circles";
		CanExportAsShapefile = false;
	}

	public EllipseFeatureLayer(string name, string filename)
		: base(name, filename)
	{
		SimpleFeatureTypeName = "Ellipse";
		HDFGroup = "/Circles";
		CanExportAsShapefile = false;
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
				HDF5Storage.ReadEllipseFeatures(this, h5Reader, HDFGroup + "/Circles");
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
			string datasetName = HDFGroup + "/Circles";
			HDF5Storage.WriteEllipseFeatures(this, h5Writer, datasetName);
			string datasetName2 = HDFGroup + "/Attributes";
			WriteManagedColumns(h5Writer, datasetName2);
			h5Writer.WriteAttribute(HDFGroup, "Type", "Circle");
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

	public IEnumerable<Ellipse> Circles()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return Ellipse(i);
			}
		}
	}

	public DataRow AddFeature(Ellipse circ)
	{
		return AddFeature((AffineFeature)circ);
	}

	public Ellipse Ellipse(int fid)
	{
		return (Ellipse)Feature(fid);
	}

	public override List<Type> AvailableFeatureTypes()
	{
		return new Type[1] { typeof(Ellipse) }.ToList();
	}

	private void UnusedLoadSaveCompoundCircles()
	{
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureDeleted)
		{
			return;
		}
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			Ellipse(featureIndicesChanged).MakeModificationBoundingBox(Ellipse(featureIndicesChanged).Center);
		}
	}

	public double GetRadiusX(int fid)
	{
		return Ellipse(fid).RadiusX;
	}

	public double GetRadiusY(int fid)
	{
		return Ellipse(fid).RadiusY;
	}
}
