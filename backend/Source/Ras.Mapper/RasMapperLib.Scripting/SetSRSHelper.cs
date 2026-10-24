using System;
using System.IO;
using System.Xml;
using Geospatial.GDALAssist;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Scripting;

public class SetSRSHelper : IDisposable
{
	private string _initialSRSFilename;

	private string _tempSRSFilename;

	public SetSRSHelper(TerrainLayer trLayer, bool forceOverwriteSRSFilename = false)
	{
		_initialSRSFilename = SharedData.SRSFilename;
		if (SharedData.IsSRSFilenameOK() && !forceOverwriteSRSFilename)
		{
			return;
		}
		Projection projection = null;
		try
		{
			using GDALRaster gDALRaster = new GDALRaster(trLayer.get_RasterFilename(0));
			projection = gDALRaster.GetProjection();
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
		if (projection != null && projection.IsValid() != SRSValidation.Corrupt)
		{
			_tempSRSFilename = Path.GetTempFileName();
			projection.ExportEsri(_tempSRSFilename);
			SharedData.SRSFilename = _tempSRSFilename;
		}
	}

	public SetSRSHelper(string rasmapFile, bool forceOverwriteSRSFilename = false)
	{
		_initialSRSFilename = SharedData.SRSFilename;
		if (!SharedData.IsSRSFilenameOK() || forceOverwriteSRSFilename)
		{
			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.Load(rasmapFile);
			SharedData.SRSFilename = RasMapperCom.GetSRSFromRasmapDoc(xmlDocument, rasmapFile);
		}
	}

	public void Dispose()
	{
		try
		{
			if (!string.IsNullOrEmpty(_tempSRSFilename) && File.Exists(_tempSRSFilename))
			{
				File.Delete(_tempSRSFilename);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
		SharedData.SRSFilename = _initialSRSFilename;
	}

	void IDisposable.Dispose()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Dispose
		this.Dispose();
	}
}
