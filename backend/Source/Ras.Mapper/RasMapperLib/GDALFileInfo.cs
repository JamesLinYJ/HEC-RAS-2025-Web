using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class GDALFileInfo
{
	private enum RasterType
	{
		None,
		Interpolated,
		DiscreteByte,
		Image,
		Unknown
	}

	private string _filename;

	private string[] _lines;

	private string _stdOut;

	private string _stdErr;

	private RasterType _rasterType;

	private bool _IsValidRaster;

	public int BandCount;

	public bool IsValidRaster => _IsValidRaster;

	public GDALFileInfo(string filename)
	{
		_stdOut = "";
		_stdErr = "";
		_IsValidRaster = false;
		_filename = filename;
		switch (Path.GetExtension(filename).ToLower())
		{
		case ".shp":
		case ".gml":
		case ".kml":
			Executables.OgrInfo(" -al -so \"" + filename + "\"", out _stdOut, out _stdErr);
			_IsValidRaster = false;
			break;
		default:
		{
			Executables.GdalInfo("\"" + filename + "\"", out _stdOut, out _stdErr);
			string toLower = _stdErr.ToLower();
			if (Operators.CompareString(toLower, "", TextCompare: false) == 0 || toLower.Contains("no errors") || !toLower.Contains("error"))
			{
				_IsValidRaster = true;
			}
			break;
		}
		}
		_lines = _stdOut.Split('\r');
		if (!_IsValidRaster)
		{
			_rasterType = RasterType.None;
			return;
		}
		string line = FindLine("Band 1 Block=");
		if (Operators.CompareString(line, "", TextCompare: false) == 0)
		{
			_rasterType = RasterType.None;
		}
		else if (line.Contains("Type=Float32") || line.Contains("Type=Int16") || line.Contains("Type=Int32") || line.Contains("Type=Float64"))
		{
			_rasterType = RasterType.Interpolated;
		}
		else if (line.Contains("Type=Byte"))
		{
			if (Operators.CompareString(FindLine("Band 3 Block="), "", TextCompare: false) == 0)
			{
				_rasterType = RasterType.DiscreteByte;
			}
			else
			{
				_rasterType = RasterType.Image;
			}
		}
		else
		{
			_rasterType = RasterType.Unknown;
		}
		using GDALRaster gDALRaster = new GDALRaster(filename);
		BandCount = gDALRaster.GetBandCount();
	}

	public int FindLineIndex(string key)
	{
		checked
		{
			int count = _lines.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (_lines[i].Contains(key))
				{
					return i;
				}
			}
			return -1;
		}
	}

	public string FindLine(string key)
	{
		int lineIndex = FindLineIndex(key);
		if (lineIndex > -1)
		{
			return _lines[lineIndex];
		}
		return "";
	}

	public bool IsWMSLayer()
	{
		return _lines[0].Contains("WMS/OGC Web Map Service");
	}

	public bool IsImage()
	{
		return _rasterType == RasterType.Image;
	}

	public bool IsDiscreteByte()
	{
		return _rasterType == RasterType.DiscreteByte;
	}

	public bool IsInterpolated()
	{
		return _rasterType == RasterType.Interpolated;
	}
}
