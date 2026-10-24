using System;
using System.IO;
using System.Runtime.InteropServices;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;

namespace RasMapperLib;

public class DiscreteValueLayer : ImageLayer
{
	private new const string DefaultLayerName = "Discrete Layer";

	protected int[] _mapValues;

	private bool _hasNoData;

	private int _noDataValue;

	private bool _checkedNoData;

	public DiscreteValueLayer()
		: this("Discrete Layer")
	{
	}

	public DiscreteValueLayer(string name)
		: base(name)
	{
	}

	public DiscreteValueLayer(string name, string filename)
		: base(name, filename)
	{
	}

	public virtual bool TryGetNoDataValue(ref int noData)
	{
		bool result;
		if (_checkedNoData)
		{
			noData = _noDataValue;
			result = _hasNoData;
		}
		else if (!File.Exists(SourceFilename))
		{
			result = false;
		}
		else
		{
			_checkedNoData = true;
			try
			{
				using GDALRaster gDALRaster = new GDALRaster(SourceFilename);
				double[] noDataVB = gDALRaster.GetNoDataVB();
				if (noDataVB.IsNullOrEmpty())
				{
					_hasNoData = false;
					result = false;
				}
				else
				{
					double noDataVB2 = noDataVB[0];
					if (double.IsNaN(noDataVB2))
					{
						_hasNoData = false;
						result = false;
					}
					else if (noDataVB2 > 2147483647.0 || noDataVB2 < -2147483648.0)
					{
						_hasNoData = false;
						result = false;
					}
					else
					{
						_noDataValue = checked((int)System.Math.Round(noDataVB2));
						_hasNoData = true;
						noData = _noDataValue;
						result = true;
					}
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = false;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public bool Rasterize(RasterM raster, ref int[] values, ref PixelRange validRegion)
	{
		bool result;
		try
		{
			if (!File.Exists(SourceFilename))
			{
				ErrorMessage = "Unable to find file:'" + SourceFilename + "'";
				result = false;
			}
			else
			{
				using GDALRaster gdr = new GDALRaster(SourceFilename);
				string errorMessage = "";
				result = Rasterize(gdr, raster, ref values, ref validRegion, ref errorMessage);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ErrorMessage = "Error getting data from file: " + SourceFilename.InSingleQuotes() + ex2.Message;
			result = false;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	public static bool Rasterize(GDALRaster gdr, RasterM raster, ref int[] values, ref PixelRange validRegion, [Optional][DefaultParameterValue("")] ref string errorMessage)
	{
		try
		{
			_ = SharedData.SRSProjection;
			Converter.Convert(raster);
			throw new NotImplementedException();
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			errorMessage = "Error getting data from file: " + gdr.Filename.InSingleQuotes() + ex2.Message;
			bool result = false;
			ProjectData.ClearProjectError();
			return result;
		}
	}
}
