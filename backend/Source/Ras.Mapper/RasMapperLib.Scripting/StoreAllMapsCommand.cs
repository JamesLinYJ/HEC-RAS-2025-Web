using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib.Scripting;

public class StoreAllMapsCommand : Command
{
	private const string sNoStoredMaps = "There are no stored maps setup for this plan.";

	public const string sRasMapFilename = "RasMapFilename";

	public const string sResultFilename = "ResultFilename";

	public override string CommandName { get; }

	public string RasMapFilename { get; set; }

	public string ResultFilename { get; set; }

	public StoreAllMapsCommand()
	{
		CommandName = "StoreAllMaps";
		RasMapFilename = "";
		ResultFilename = "";
	}

	public StoreAllMapsCommand(string rmFilename, string resFilename = "")
	{
		CommandName = "StoreAllMaps";
		RasMapFilename = "";
		ResultFilename = "";
		RasMapFilename = rmFilename;
		ResultFilename = resFilename;
	}

	public override void Execute(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (!ValidateArguments(prog))
		{
			prog.ReportMessage("Error executing: " + CommandName);
			return;
		}
		prog.ReportMessage("Computing Stored Results Maps");
		checked
		{
			using (new SetSRSHelper(RasMapFilename))
			{
				SharedData.RasMapFilename = RasMapFilename;
				int ordinal = 0;
				XElement xElement = XElement.Load(RasMapFilename);
				if (xElement == null)
				{
					prog.ReportError("Malformed Rasmap file " + RasMapFilename.InSingleQuotes());
					return;
				}
				XElement xElement2 = xElement.Element("Results");
				if (xElement2 == null)
				{
					prog.ReportError("Malformed Rasmap file " + RasMapFilename.InSingleQuotes());
					return;
				}
				foreach (XElement element in xElement2.Elements("Layer"))
				{
					XAttribute xAttribute = element.Attribute("Filename");
					if (xAttribute == null)
					{
						continue;
					}
					string makeAbsolutePathTerm = xAttribute.Value;
					if (!Matches(makeAbsolutePathTerm))
					{
						continue;
					}
					makeAbsolutePathTerm = RasMapperLib.Utilities.Path.MakeAbsolute(makeAbsolutePathTerm, RasMapFilename);
					if (!File.Exists(makeAbsolutePathTerm))
					{
						prog.ReportError("Result file does not exist " + makeAbsolutePathTerm.InSingleQuotes());
						continue;
					}
					try
					{
						RASResults rASResults = new RASResults(makeAbsolutePathTerm);
						if (rASResults.Geometry.Terrain == null)
						{
							prog.ReportError("No Terrain association found for " + System.IO.Path.GetFileName(makeAbsolutePathTerm).InSingleQuotes());
							continue;
						}
						int ordinal2 = ordinal;
						foreach (XElement xElement3 in from lyr in element.Elements("Layer")
							where (string?)lyr.Attribute("Type") == "RASResultsMap"
							select lyr)
						{
							RASResultsMap rASResultsMap = new RASResultsMap(rASResults);
							XmlNode xmlNode = new XmlDocument().ReadNode(xElement3.CreateReader());
							rASResultsMap.XMLLoad(xmlNode as XmlElement);
							if (!rASResultsMap.OutputModeIsStoredType)
							{
								continue;
							}
							try
							{
								if (rASResultsMap.StoreMap(prog, showFinishedMessage: false))
								{
									ordinal++;
								}
							}
							catch (Exception ex)
							{
								ProjectData.SetProjectError(ex);
								Exception ex2 = ex;
								prog.ReportError("Store-Map error from " + System.IO.Path.GetFileName(makeAbsolutePathTerm).InSingleQuotes() + ", " + ex2.Message);
								ProjectData.ClearProjectError();
							}
						}
						prog.ReportMessage(ordinal - ordinal2 + " Maps generated for " + System.IO.Path.GetFileName(makeAbsolutePathTerm).InSingleQuotes());
					}
					catch (Exception ex3)
					{
						ProjectData.SetProjectError(ex3);
						Exception ex4 = ex3;
						prog.ReportError("Result error " + makeAbsolutePathTerm.InSingleQuotes() + ", " + ex4.Message);
						ProjectData.ClearProjectError();
					}
				}
			}
		}
	}

	private bool Matches(string resFileXML)
	{
		if (string.IsNullOrEmpty(ResultFilename))
		{
			return true;
		}
		if (Operators.CompareString(System.IO.Path.GetFileName(resFileXML).ToLowerInvariant(), System.IO.Path.GetFileName(ResultFilename).ToLowerInvariant(), TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	protected override void XMLSaveTags(XElement ele)
	{
		if (!string.IsNullOrEmpty(RasMapFilename))
		{
			ele.Add(new XElement("RasMapFilename", RasMapFilename));
		}
		if (!string.IsNullOrEmpty(ResultFilename))
		{
			ele.Add(new XElement("ResultFilename", ResultFilename));
		}
	}

	protected override void XMLLoadTags(XElement ele)
	{
		XElement xElement = ele.Element("RasMapFilename");
		if (xElement != null)
		{
			RasMapFilename = xElement.Value;
		}
		XElement xElement2 = ele.Element("ResultFilename");
		if (xElement2 != null)
		{
			ResultFilename = xElement2.Value;
		}
	}

	public override void ExecuteWith(Dictionary<string, string> kvpArgs, List<string> otherArgs, ProgressReporter prog = null)
	{
		string rasMapFilename = "";
		string resultFilename = "";
		foreach (KeyValuePair<string, string> kvpArg in kvpArgs)
		{
			if (kvpArg.Key.RoughlyEquals("RasMapFilename"))
			{
				rasMapFilename = kvpArg.Value;
			}
			else if (kvpArg.Key.RoughlyEquals("ResultFilename"))
			{
				resultFilename = kvpArg.Value;
			}
		}
		if (string.IsNullOrEmpty(rasMapFilename))
		{
			foreach (string otherArg in otherArgs)
			{
				if (File.Exists(otherArg) && otherArg.RoughlyEndsWith(".rasmap"))
				{
					rasMapFilename = otherArg;
					break;
				}
			}
		}
		if (string.IsNullOrEmpty(resultFilename))
		{
			foreach (string otherArg2 in otherArgs)
			{
				if (File.Exists(otherArg2) && (otherArg2.RoughlyEndsWith(".hdf") || otherArg2.RoughlyEndsWith(".h5")))
				{
					resultFilename = otherArg2;
					break;
				}
			}
		}
		RasMapFilename = rasMapFilename;
		ResultFilename = resultFilename;
		Execute(prog);
	}

	public override bool ValidateArguments(ProgressReporter prog = null)
	{
		bool result;
		if (File.Exists(RasMapFilename))
		{
			result = true;
		}
		else
		{
			prog?.ReportError("RasMapFilename " + RasMapFilename.InSingleQuotes() + " does not exist.");
			result = false;
		}
		if (string.IsNullOrEmpty(ResultFilename))
		{
			return result;
		}
		if (File.Exists(ResultFilename))
		{
			return result;
		}
		prog?.ReportError("ResultFilename " + ResultFilename.InSingleQuotes() + " does not exist.");
		return false;
	}
}
