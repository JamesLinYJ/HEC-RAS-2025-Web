using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

public class PierFeatureLayer : AffineFeatureLayer
{
	public const string PierType = "Pier";

	public virtual string HDFGroup { get; }

	public virtual string HDFDataset { get; }

	public override bool CanExportAsShapefile { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public new Pier this[int index] => Pier(index);

	public PierFeatureLayer()
		: base("Pier Layer")
	{
		HDFGroup = "/Piers";
		HDFDataset = HDFGroup + "/Piers";
		CanExportAsShapefile = false;
		SimpleFeatureTypeName = "Pier";
	}

	public PierFeatureLayer(string name)
		: base(name)
	{
		HDFGroup = "/Piers";
		HDFDataset = HDFGroup + "/Piers";
		CanExportAsShapefile = false;
		SimpleFeatureTypeName = "Pier";
	}

	public PierFeatureLayer(string name, string filename)
		: base(name, filename)
	{
		HDFGroup = "/Piers";
		HDFDataset = HDFGroup + "/Piers";
		CanExportAsShapefile = false;
		SimpleFeatureTypeName = "Pier";
	}

	public PierFeatureLayer(string name, DataTable useFeatureTable)
		: base(name, useFeatureTable)
	{
		HDFGroup = "/Piers";
		HDFDataset = HDFGroup + "/Piers";
		CanExportAsShapefile = false;
		SimpleFeatureTypeName = "Pier";
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
				HDF5Storage.ReadPierFeatures(this, h5Reader, HDFDataset);
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
			HDF5Storage.WritePierFeatures(this, h5Writer, HDFDataset);
			string datasetName = HDFGroup + "/Attributes";
			WriteManagedColumns(h5Writer, datasetName);
			h5Writer.WriteAttribute(HDFGroup, "Type", "Pier");
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

	public IEnumerable<Pier> Piers()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return Pier(i);
			}
		}
	}

	public double GetWidth(int fid)
	{
		if (Pier(fid).Body != null)
		{
			return Pier(fid).Body.Width;
		}
		if (Pier(fid).TopCap != null)
		{
			if (Pier(fid).TopCap is Ellipse)
			{
				return ((Ellipse)Pier(fid).TopCap).RadiusX * 2.0;
			}
			if (Pier(fid).TopCap is Triangle)
			{
				return ((Triangle)Pier(fid).TopCap).Width;
			}
			throw new NotImplementedException("Unsupported type for top cap");
		}
		if (Pier(fid).BottomCap != null)
		{
			if (Pier(fid).BottomCap is Ellipse)
			{
				return ((Ellipse)Pier(fid).BottomCap).RadiusX * 2.0;
			}
			if (Pier(fid).BottomCap is Triangle)
			{
				return ((Triangle)Pier(fid).BottomCap).Width;
			}
			throw new NotImplementedException("Unsupported type for bottom cap");
		}
		return 0.0;
	}

	public double GetBodyHeight(int fid)
	{
		if (Pier(fid).Body != null)
		{
			return Pier(fid).Body.Height;
		}
		return 0.0;
	}

	public Type GetTopCapType(int fid)
	{
		if (Pier(fid).TopCap != null)
		{
			return Pier(fid).TopCap.GetType();
		}
		return null;
	}

	public Type GetBotCapType(int fid)
	{
		if (Pier(fid).BottomCap != null)
		{
			return Pier(fid).BottomCap.GetType();
		}
		return null;
	}

	public double GetTopCapRadiusY(int fid)
	{
		if (Pier(fid).TopCap != null)
		{
			if (Pier(fid).TopCap is Ellipse)
			{
				return ((Ellipse)Pier(fid).TopCap).RadiusY;
			}
			if (Pier(fid).TopCap is Triangle)
			{
				return ((Triangle)Pier(fid).TopCap).Height / 2.0;
			}
			throw new NotImplementedException("Unsupported type for top cap");
		}
		return 0.0;
	}

	public double GetBottomCapRadiusY(int fid)
	{
		if (Pier(fid).BottomCap != null)
		{
			if (Pier(fid).BottomCap is Ellipse)
			{
				return ((Ellipse)Pier(fid).BottomCap).RadiusY;
			}
			if (Pier(fid).BottomCap is Triangle)
			{
				return ((Triangle)Pier(fid).BottomCap).Height / 2.0;
			}
			throw new NotImplementedException("Unsupported type for bottom cap");
		}
		return 0.0;
	}

	public DataRow AddFeature(Pier rect)
	{
		return AddFeature((AffineFeature)rect);
	}

	public Pier Pier(int fid)
	{
		return (Pier)Feature(fid);
	}

	public override List<Type> AvailableFeatureTypes()
	{
		return new Type[1] { typeof(RectangleFeature) }.ToList();
	}
}
