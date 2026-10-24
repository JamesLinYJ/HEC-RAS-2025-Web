using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using RasMapperLib.TinHelpers;
using Utility.Extensions;

namespace RasMapperLib;

public class SMSFeatureLayer : TinFeatureLayer
{
	public SMSFeatureLayer()
		: base("SMS Feature Layer")
	{
	}

	public SMSFeatureLayer(string name)
		: base(name)
	{
	}

	public SMSFeatureLayer(string name, string filename)
		: base(name, filename)
	{
		AddWSELChild();
		AddVelChild();
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAutoNameColumn();
	}

	protected override void XMLLoadChildren(XmlElement xmlEl)
	{
		XmlNodeList xmlNodeList = xmlEl.SelectNodes("Layer");
		bool condition = false;
		bool condition2 = false;
		foreach (XmlElement xmlNodeList2 in xmlNodeList)
		{
			if (Layer.GetTypeNameFromXML(xmlNodeList2).RoughlyEquals("SMSResultLayer"))
			{
				string relativeFilenameFromXML = Layer.GetRelativeFilenameFromXML(xmlNodeList2);
				if (Path.GetFileName(relativeFilenameFromXML).RoughlyEquals("fort63.h5"))
				{
					SMSResultLayer sMSResultLayer = new SMSResultLayer(this, Path.Combine(Path.GetDirectoryName(SourceFilename), "fort63.h5"));
					sMSResultLayer.XMLLoad(xmlNodeList2);
					Nodes.Add(sMSResultLayer);
					condition = true;
				}
				else if (Path.GetFileName(relativeFilenameFromXML).RoughlyEquals("fort64.h5"))
				{
					SMSResultLayer sMSResultLayer2 = new SMSResultLayer(this, Path.Combine(Path.GetDirectoryName(SourceFilename), "fort64.h5"));
					sMSResultLayer2.XMLLoad(xmlNodeList2);
					Nodes.Add(sMSResultLayer2);
					condition2 = true;
				}
			}
		}
		if (!condition)
		{
			AddWSELChild();
		}
		if (!condition2)
		{
			AddVelChild();
		}
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		if (FeatureCount() > 1)
		{
			throw new NotImplementedException("Only one feature allowed per SMS layer at this time.");
		}
	}

	protected override void LoadFeatureTable()
	{
		if (!string.IsNullOrEmpty(SourceFilename) && File.Exists(SourceFilename))
		{
			ImportSMSMeshDefinition();
		}
	}

	protected override bool SaveFeatureTableInternal()
	{
		if (string.IsNullOrEmpty(SourceFilename))
		{
			return false;
		}
		return ExportAsSMSMeshDefinition();
	}

	public bool ExportAsSMSMeshDefinition()
	{
		string sourceFilename = SourceFilename;
		if (FeatureCount() == 0)
		{
			return false;
		}
		if (FeatureCount() > 1)
		{
			return false;
		}
		Tin tin = Tin(0);
		int pointMCount = tin.PointMCount();
		int triangleCount = tin.TriangleCount();
		if (File.Exists(sourceFilename))
		{
			File.Delete(sourceFilename);
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(GetFeatureName(0));
		stringBuilder.AppendLine(triangleCount + "\t" + pointMCount);
		checked
		{
			int count = pointMCount - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = tin.PointM(i);
				stringBuilder.AppendLine("\t\t" + (i + 1) + " " + pointM.X + " " + pointM.Y + "\t" + pointM.Z);
			}
			int count2 = triangleCount - 1;
			for (int j = 0; j <= count2; j++)
			{
				TriangleStruct triangleStruct = tin.Triangle(j);
				stringBuilder.AppendLine("\t" + (j + 1) + "\t3\t" + (triangleStruct.p0 + 1) + "\t" + (triangleStruct.p1 + 1) + "\t" + (triangleStruct.p2 + 1));
			}
			File.WriteAllText(sourceFilename, stringBuilder.ToString());
			return true;
		}
	}

	public bool SaveResultChildren(RASResults correspondingResult, int meshIdx)
	{
		string guid = Guid.NewGuid().ToString();
		Nodes.Add(SMSResultLayer.CreateWaterSurfaceLayer(this, correspondingResult, meshIdx, guid));
		Nodes.Add(SMSResultLayer.CreateVelocityLayer(this, correspondingResult, meshIdx, guid));
		return true;
	}

	public void ImportSMSMeshDefinition()
	{
		if (!File.Exists(SourceFilename))
		{
			ErrorMessage = "Source file " + SourceFilename.InSingleQuotes() + " does not exist.";
			return;
		}
		string[] allLines = File.ReadAllLines(SourceFilename);
		if (allLines.Count() < 6)
		{
			ErrorMessage = "Malformed SMS Grid File";
			return;
		}
		string newName = allLines[0];
		int result = 0;
		int result2 = 0;
		_ = allLines[1];
		string[] parts = allLines[1].Split(new string[2] { " ", "\t" }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 2)
		{
			ErrorMessage = "Malformed SMS Grid File";
			return;
		}
		if (!int.TryParse(parts[0], out result2))
		{
			ErrorMessage = "Malformed SMS Grid File";
			return;
		}
		if (!int.TryParse(parts[1], out result))
		{
			ErrorMessage = "Malformed SMS Grid File";
			return;
		}
		PointMs pointMs = new PointMs();
		checked
		{
			int count = 2 + result - 1;
			for (int i = 2; i <= count; i++)
			{
				string[] partsArray = allLines[i].Split(new string[2] { " ", "\t" }, StringSplitOptions.RemoveEmptyEntries);
				if (partsArray.Length != 4)
				{
					ErrorMessage = "Malformed SMS Grid File";
					return;
				}
				if (!double.TryParse(partsArray[1], out var result3))
				{
					ErrorMessage = "Malformed SMS Grid File";
					return;
				}
				if (!double.TryParse(partsArray[2], out var result4))
				{
					ErrorMessage = "Malformed SMS Grid File";
					return;
				}
				if (!double.TryParse(partsArray[3], out var result5))
				{
					ErrorMessage = "Malformed SMS Grid File";
					return;
				}
				pointMs.Add(new PointM(result3, result4, result5));
			}
			List<TriangleStruct> triangleStructs = new List<TriangleStruct>();
			int ordinal = 2 + result;
			int count2 = 2 + result + result2 - 1;
			for (int j = ordinal; j <= count2; j++)
			{
				string[] parts2 = allLines[j].Split(new string[2] { " ", "\t" }, StringSplitOptions.RemoveEmptyEntries);
				if (parts2.Length != 5)
				{
					ErrorMessage = "Malformed SMS Grid File";
					return;
				}
				if (!int.TryParse(parts2[2], out var result6))
				{
					ErrorMessage = "Malformed SMS Grid File";
					return;
				}
				if (!int.TryParse(parts2[3], out var result7))
				{
					ErrorMessage = "Malformed SMS Grid File";
					return;
				}
				if (!int.TryParse(parts2[4], out var result8))
				{
					ErrorMessage = "Malformed SMS Grid File";
					return;
				}
				TriangleStruct triangleStruct = default;
				triangleStruct.AssignPoints(result6 - 1, result7 - 1, result8 - 1);
				triangleStructs.Add(triangleStruct);
			}
			AddFeature(new Tin(pointMs, triangleStructs.ToArray()));
			SetFeatureName(0, newName);
		}
	}

	private void AddWSELChild()
	{
		Nodes.Add(new SMSResultLayer(this, Path.Combine(Path.GetDirectoryName(SourceFilename), "fort63.h5")));
	}

	private void AddVelChild()
	{
		Nodes.Add(new SMSResultLayer(this, Path.Combine(Path.GetDirectoryName(SourceFilename), "fort64.h5")));
	}
}
