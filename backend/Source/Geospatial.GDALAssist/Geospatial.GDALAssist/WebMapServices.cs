using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using Geospatial.Rasters;

namespace Geospatial.GDALAssist;

public static class WebMapServices
{
	private static HttpClient _client = new HttpClient();

	public static async Task<Stream> GetServerIcon(string WMSFile)
	{
		string textValue = "";
		if (Path.GetExtension(WMSFile) == ".xml")
		{
			try
			{
				XmlTextReader xmlTextReader = new XmlTextReader(WMSFile);
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.Load(xmlTextReader);
				foreach (XmlNode elementsByTagName in xmlDocument.GetElementsByTagName("ServerUrl"))
				{
					textValue = elementsByTagName.InnerText;
				}
				xmlTextReader.Close();
			}
			catch
			{
			}
			if (textValue == "")
			{
				return null;
			}
			if (textValue.Contains(".com/"))
			{
				string part = null;
				string[] parts = textValue.Split('.');
				for (int i = 0; i < parts.Length; i++)
				{
					if (parts[i].Substring(0, 3) == "com")
					{
						part = parts[i - 1];
						break;
					}
				}
				if (part == null)
				{
					return null;
				}
				textValue = textValue.Substring(textValue.IndexOf(part), part.Length + 4);
			}
			else if (textValue.Contains(".net/"))
			{
				string part2 = null;
				string[] partsArray = textValue.Split('.');
				for (int j = 0; j < partsArray.Length; j++)
				{
					if (partsArray[j].Substring(0, 3) == "net")
					{
						part2 = partsArray[j - 1];
						break;
					}
				}
				if (part2 == null)
				{
					return null;
				}
				textValue = textValue.Substring(textValue.IndexOf(part2), part2.Length + 4);
			}
			else if (textValue.Contains(".org/"))
			{
				string part3 = null;
				string[] parts2 = textValue.Split('.');
				for (int k = 0; k < parts2.Length; k++)
				{
					if (parts2[k].Substring(0, 3) == "org")
					{
						part3 = parts2[k - 1];
						break;
					}
				}
				if (part3 == null)
				{
					return null;
				}
				textValue = textValue.Substring(textValue.IndexOf(part3), part3.Length + 4);
			}
			else
			{
				if (!textValue.Contains(".gov/"))
				{
					return null;
				}
				string part4 = null;
				string[] parts3 = textValue.Split('.');
				for (int l = 0; l < parts3.Length; l++)
				{
					if (parts3[l].Substring(0, 3) == "gov")
					{
						part4 = parts3[l - 1];
						break;
					}
				}
				if (part4 == null)
				{
					return null;
				}
				textValue = textValue.Substring(textValue.IndexOf(part4), part4.Length + 4);
			}
			string requestUri = "http://" + textValue + "/favicon.ico";
			try
			{
				using HttpResponseMessage response = await _client.GetAsync(requestUri);
				if (response.IsSuccessStatusCode)
				{
					Stream stream = await response.Content.ReadAsStreamAsync();
					MemoryStream memoryStream = new MemoryStream();
					stream.CopyTo(memoryStream);
					return memoryStream;
				}
				return null;
			}
			catch
			{
				return null;
			}
		}
		throw new Exception("Not an XML file.");
	}

	public static bool IsServerActive(string XMLFileName)
	{
		try
		{
			using GDALRaster gDALRaster = new GDALRaster(XMLFileName);
			if (gDALRaster.GetDataType() == typeof(byte))
			{
				BandedRasterData<byte> bandedRasterData = gDALRaster.ReadRaster(RasterDefinition.ComputeBoundingRaster(gDALRaster.GetFullNativeExtents(), 256), (IList<int>)null, (byte)0);
				if (bandedRasterData == null)
				{
					return false;
				}
				foreach (byte[] band in bandedRasterData.Bands)
				{
					if (band == null)
					{
						return false;
					}
				}
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	public static string[] GetWMSFileList()
	{
		List<string> files = Directory.GetFiles(GDALSetup.XMLDirectory, "*.xml").ToList();
		for (int i = 0; i < files.Count; i++)
		{
			try
			{
				XmlTextReader xmlTextReader = new XmlTextReader(files[i]);
				new XmlDocument().Load(xmlTextReader);
				xmlTextReader.Close();
			}
			catch
			{
				files.RemoveAt(i);
				i--;
			}
		}
		return files.ToArray();
	}

	public static void GenerateWMS(string BaseServerURL, bool overwrite = true)
	{
		string StdOut = "";
		string StdErr = "";
		Executables.GdalInfo("\"WMS:" + BaseServerURL + "\"", out StdOut, out StdErr);
		bool condition = true;
		bool condition2 = false;
		string layeredURL = "";
		string blockX = "512";
		string blockY = "512";
		StdOut = StdOut.Replace("\r", "");
		string[] parts = StdOut.Split('\n');
		foreach (string part in parts)
		{
			string textValue = "Size is ";
			if (part.Length > textValue.Length && part.Substring(0, textValue.Length) == textValue)
			{
				string[] partsArray = part.Substring(textValue.Length).Replace(" ", "").Split(',');
				blockX = partsArray[0];
				blockY = partsArray[1];
			}
			if (condition2)
			{
				condition2 = false;
				continue;
			}
			try
			{
				if (part.Contains("SUBDATASET_"))
				{
					if (condition)
					{
						layeredURL = part.Substring(part.IndexOf("NAME=WMS:") + 5);
					}
					if (!condition)
					{
						GenerateXML(BaseServerURL, part.Substring(part.IndexOf("DESC=") + 5), layeredURL, blockX, blockY, overwrite);
					}
					condition = !condition;
				}
			}
			catch
			{
				if (condition)
				{
					condition2 = true;
				}
			}
		}
	}

	private static void GenerateXML(string baseServerURL, string description, string layeredURL, string blockX, string blockY, bool overwrite)
	{
		description = description.Replace(' ', '_').Replace('.', '_').Replace('/', '-');
		string xmlDirectoryGdalSetupTerm = GDALSetup.XMLDirectory + "\\" + description;
		int ordinal = 0;
		try
		{
			if (overwrite)
			{
				File.Delete(xmlDirectoryGdalSetupTerm + ".xml");
			}
		}
		catch
		{
		}
		if (File.Exists(xmlDirectoryGdalSetupTerm + ".xml"))
		{
			while (File.Exists(xmlDirectoryGdalSetupTerm + ordinal + ".xml"))
			{
				ordinal++;
			}
			xmlDirectoryGdalSetupTerm += ordinal;
		}
		xmlDirectoryGdalSetupTerm += ".xml";
		XmlDocument xmlDocument = new XmlDocument();
		XmlElement xmlElement = xmlDocument.CreateElement("GDAL_WMS");
		XmlElement xmlElement2 = xmlDocument.CreateElement("Service");
		XmlAttribute xmlAttribute = xmlDocument.CreateAttribute("Name");
		xmlAttribute.Value = "WMS";
		xmlElement2.Attributes.Append(xmlAttribute);
		XmlElement xmlElement3 = xmlDocument.CreateElement("Version");
		xmlElement3.InnerText = GetURLVar(layeredURL, "Version");
		xmlElement2.AppendChild(xmlElement3);
		XmlElement xmlElement4 = xmlDocument.CreateElement("ServerUrl");
		xmlElement4.InnerText = baseServerURL;
		xmlElement2.AppendChild(xmlElement4);
		string innerText = "";
		if (xmlElement3.InnerText == "1.3.0")
		{
			XmlElement xmlElement5 = xmlDocument.CreateElement("CRS");
			xmlElement5.InnerText = GetURLVar(layeredURL, "CRS");
			xmlElement2.AppendChild(xmlElement5);
			innerText = xmlElement5.InnerText;
		}
		else
		{
			XmlElement xmlElement6 = xmlDocument.CreateElement("SRS");
			xmlElement6.InnerText = GetURLVar(layeredURL, "SRS");
			xmlElement2.AppendChild(xmlElement6);
			innerText = xmlElement6.InnerText;
		}
		XmlElement xmlElement7 = xmlDocument.CreateElement("ImageFormat");
		xmlElement7.InnerText = "image/png";
		xmlElement2.AppendChild(xmlElement7);
		XmlElement xmlElement8 = xmlDocument.CreateElement("Layers");
		xmlElement8.InnerText = GetURLVar(layeredURL, "layers");
		xmlElement2.AppendChild(xmlElement8);
		xmlElement.AppendChild(xmlElement2);
		XmlElement xmlElement9 = xmlDocument.CreateElement("DataWindow");
		XmlElement xmlElement10 = xmlDocument.CreateElement("UpperLeftX");
		XmlElement xmlElement11 = xmlDocument.CreateElement("UpperLeftY");
		XmlElement xmlElement12 = xmlDocument.CreateElement("LowerRightX");
		XmlElement xmlElement13 = xmlDocument.CreateElement("LowerRightY");
		string[] parts = GetURLVar(layeredURL, "bbox").Split(',');
		if (parts.Length != 4)
		{
			throw new Exception("BBOX incorrect.");
		}
		xmlElement10.InnerText = parts[0];
		xmlElement11.InnerText = parts[3];
		xmlElement12.InnerText = parts[2];
		xmlElement13.InnerText = parts[1];
		xmlElement9.AppendChild(xmlElement10);
		xmlElement9.AppendChild(xmlElement11);
		xmlElement9.AppendChild(xmlElement12);
		xmlElement9.AppendChild(xmlElement13);
		XmlElement xmlElement14 = xmlDocument.CreateElement("SizeX");
		XmlElement xmlElement15 = xmlDocument.CreateElement("SizeY");
		string StdOut = "";
		string StdErr = "";
		Executables.GdalInfo("\"WMS:" + layeredURL + "\"", out StdOut, out StdErr);
		StdOut = StdOut.Replace("\r", "");
		string[] partsArray = StdOut.Split('\n');
		string[] parts2 = partsArray;
		foreach (string part in parts2)
		{
			string textValue = "Size is ";
			if (part.Length > textValue.Length && part.Substring(0, textValue.Length) == textValue)
			{
				string[] parts3 = part.Substring(textValue.Length).Replace(" ", "").Split(',');
				xmlElement14.InnerText = parts3[0];
				xmlElement15.InnerText = parts3[1];
				break;
			}
		}
		xmlElement9.AppendChild(xmlElement14);
		xmlElement9.AppendChild(xmlElement15);
		xmlElement.AppendChild(xmlElement9);
		XmlElement xmlElement16 = xmlDocument.CreateElement("OverviewCount");
		parts2 = partsArray;
		foreach (string part2 in parts2)
		{
			string textValue2 = "Overviews: ";
			if (part2.Contains(textValue2))
			{
				string[] parts4 = part2.Split('x');
				if (parts4.Length != 0)
				{
					xmlElement16.InnerText = (parts4.Length - 1).ToString();
				}
				break;
			}
		}
		xmlElement.AppendChild(xmlElement16);
		XmlElement xmlElement17 = xmlDocument.CreateElement("BlockSizeX");
		XmlElement xmlElement18 = xmlDocument.CreateElement("BlockSizeY");
		xmlElement17.InnerText = blockX;
		xmlElement18.InnerText = blockY;
		xmlElement.AppendChild(xmlElement17);
		xmlElement.AppendChild(xmlElement18);
		XmlElement xmlElement19 = xmlDocument.CreateElement("Projection");
		xmlElement19.InnerText = innerText;
		xmlElement.AppendChild(xmlElement19);
		XmlElement xmlElement20 = xmlDocument.CreateElement("Cache");
		XmlElement xmlElement21 = xmlDocument.CreateElement("Path");
		xmlElement21.InnerText = GDALSetup.MappingDirectory;
		xmlElement20.AppendChild(xmlElement21);
		xmlElement.AppendChild(xmlElement20);
		xmlDocument.AppendChild(xmlElement);
		xmlDocument.Save(xmlDirectoryGdalSetupTerm);
	}

	private static string GetURLVar(string URL, string param)
	{
		param = param.ToUpper();
		string[] parts = URL.Split('&');
		foreach (string part in parts)
		{
			if (part.Substring(0, param.Length + 1) == param + "=")
			{
				return part.Substring(param.Length + 1);
			}
		}
		return null;
	}
}
