using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;
using Geospatial.GDALAssist;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using PipeClient;
using RasMapperLib.Mesh;
using RasMapperLib.Utilities;
using Utility.Progress;

namespace RasMapperLib;

public class RasMapperCom
{
	public static void LoadRasMap(string filename, string xmlText)
	{
		XmlDocument xmlDocument = new XmlDocument();
		xmlText = xmlText.Replace('\u001f', '\t');
		try
		{
			if (Operators.CompareString(xmlText, "", TextCompare: false) != 0)
			{
				xmlDocument.LoadXml(xmlText);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			MessageBox.Show("This rasmap file is corrupted.  You may have to delete the rasmap file located at " + filename + " to use RasMapper");
			xmlText = "";
			ProjectData.ClearProjectError();
		}
		XmlNodeList elementsByTagName = xmlDocument.GetElementsByTagName("CommandLineArgs");
		if (elementsByTagName != null && elementsByTagName.Count > 0)
		{
			_ = elementsByTagName[0].InnerText;
		}
		if (Operators.CompareString(XML.StringTextElement(xmlDocument.DocumentElement, "Options/Units"), "SI Units", TextCompare: false) == 0)
		{
			SharedData.Units = SharedData.EnumUnits.SIUnits;
		}
		else
		{
			SharedData.Units = SharedData.EnumUnits.USCustomary;
		}
		SharedData.PlanStructures.Clear();
		foreach (XmlNode selectNode in xmlDocument.SelectNodes("RAS/Plans/Plan"))
		{
			string stringAttribute = XML.StringAttribute(selectNode, "Filename");
			_ = stringAttribute + ".hdf";
			string title = XML.StringAttribute(selectNode, "Title");
			string stringAttribute2 = XML.StringAttribute(selectNode, "Geometry");
			string geomExt = "";
			if (!string.IsNullOrEmpty(stringAttribute2))
			{
				geomExt = System.IO.Path.GetExtension(stringAttribute2);
			}
			string stringAttribute3 = XML.StringAttribute(selectNode, "Flow");
			string flowExt = "";
			if (!string.IsNullOrEmpty(stringAttribute3))
			{
				flowExt = System.IO.Path.GetExtension(stringAttribute3);
			}
			string stringAttribute4 = XML.StringAttribute(selectNode, "StartDate");
			string stringAttribute5 = XML.StringAttribute(selectNode, "StartTime");
			if (string.IsNullOrEmpty(stringAttribute5))
			{
				stringAttribute5 = "0000";
			}
			string stringAttribute6 = XML.StringAttribute(selectNode, "EndDate");
			string stringAttribute7 = XML.StringAttribute(selectNode, "EndTime");
			if (string.IsNullOrEmpty(stringAttribute7))
			{
				stringAttribute7 = "0000";
			}
			DateTime startDate = DateTime.MinValue;
			DateTime endDate = DateTime.MaxValue;
			if (!string.IsNullOrEmpty(stringAttribute4))
			{
				try
				{
					startDate = DSS.ConvertFromDSSDateTime(stringAttribute4 + stringAttribute5);
				}
				catch (Exception ex3)
				{
					ProjectData.SetProjectError(ex3);
					Exception ex4 = ex3;
					ProjectData.ClearProjectError();
				}
			}
			if (!string.IsNullOrEmpty(stringAttribute6))
			{
				try
				{
					endDate = DSS.ConvertFromDSSDateTime(stringAttribute6 + stringAttribute7);
				}
				catch (Exception ex5)
				{
					ProjectData.SetProjectError(ex5);
					Exception ex6 = ex5;
					ProjectData.ClearProjectError();
				}
			}
			bool isActivePlan = XML.BooleanAttribute(selectNode, "Current");
			SharedData.PlanStructures.Add(new SharedData.PlanStructure(stringAttribute, title, geomExt, flowExt, startDate, endDate, isActivePlan));
		}
		if (Operators.CompareString(XML.StringTextElement(xmlDocument.DocumentElement, "Options/DebugMode"), "True", TextCompare: false) == 0)
		{
			SharedData.DebugMode = true;
		}
		int.TryParse(XML.StringTextElement(xmlDocument.DocumentElement, "Options/NotificationHwnd"), out SharedData.HWNDRas);
		if (SharedData.RasMapper == null)
		{
			SharedData.RasMapper = new RASMapper();
		}
		RASMapper rasMapper = SharedData.RasMapper;
		SharedData.RasMapperIsLoading = true;
		using (new WaitCursor(rasMapper))
		{
			rasMapper.DisablePlotLayers = true;
			if (Operators.CompareString(rasMapper.RasmapFilenameLoaded.ToLower(), filename.ToLower(), TextCompare: false) != 0)
			{
				SharedData.RasMapFilename = filename;
				if (File.Exists(filename))
				{
					try
					{
						if (SharedData.RasMapDoc != null)
						{
							rasMapper.LoadRASMapFile(SharedData.RasMapDoc, SharedData.RasMapFilename);
						}
					}
					catch (Exception projectError)
					{
						ProjectData.SetProjectError(projectError);
						SharedData.ResetParameters();
						ProjectData.ClearProjectError();
					}
				}
				else
				{
					SharedData.ResetParameters();
				}
				rasMapper.AfterLoad();
			}
			RASGeometry rASGeometry = null;
			foreach (XmlNode selectNode2 in xmlDocument.SelectNodes("/RAS/Geometries/Geometry"))
			{
				string stringAttributeXmlTerm = XML.StringAttribute(selectNode2, "Filename") + ".hdf";
				string filename2 = XML.StringAttribute(selectNode2, "Title");
				if (File.Exists(stringAttributeXmlTerm))
				{
					string title2 = RasMapperLib.Utilities.Path.EnsureFileCharacters(filename2);
					RASGeometry rASGeometry2 = rasMapper.GeometriesGroup.EnsureGeometry(title2, stringAttributeXmlTerm);
					if (XML.BooleanAttribute(selectNode2, "Current"))
					{
						rASGeometry = rASGeometry2;
					}
				}
			}
			foreach (XmlNode selectNode3 in xmlDocument.SelectNodes("/RAS/EventConditions/EventCondition"))
			{
				string stringAttributeXmlTerm2 = XML.StringAttribute(selectNode3, "Filename") + ".hdf";
				string filename3 = XML.StringAttribute(selectNode3, "Title");
				if (File.Exists(stringAttributeXmlTerm2))
				{
					string title3 = RasMapperLib.Utilities.Path.EnsureFileCharacters(filename3);
					rasMapper.EventsGroup.EnsureEvent(title3, stringAttributeXmlTerm2);
				}
			}
			RASResults rASResults = null;
			foreach (XmlNode selectNode4 in xmlDocument.SelectNodes("RAS/Plans/Plan"))
			{
				string stringAttributeXmlTerm3 = XML.StringAttribute(selectNode4, "Filename") + ".hdf";
				string toolTipText = XML.StringAttribute(selectNode4, "Title");
				bool booleanAttribute = XML.BooleanAttribute(selectNode4, "TestingAcceptedResult");
				if (!File.Exists(stringAttributeXmlTerm3))
				{
					continue;
				}
				try
				{
					RASResults rASResults2 = rasMapper.ResultsGroup.EnsureResult(stringAttributeXmlTerm3);
					rASResults2.ToolTipText = toolTipText;
					if (booleanAttribute)
					{
						rASResults2.Name += "[Accepted]";
						rASResults2.Text = rASResults2.Name;
					}
					if (XML.BooleanAttribute(selectNode4, "Current"))
					{
						rASResults = rASResults2;
					}
					System.IO.Path.GetFileNameWithoutExtension(stringAttributeXmlTerm3);
				}
				catch (Exception ex7)
				{
					ProjectData.SetProjectError(ex7);
					Exception ex8 = ex7;
					ProjectData.ClearProjectError();
				}
			}
			if (rASResults != null)
			{
				rASResults.Expand();
				if (rASResults.TreeView.SelectedNode == null)
				{
					rASResults.TreeView.SelectedNode = rASResults;
				}
			}
			else if (rASGeometry != null)
			{
				rASGeometry.Expand();
				if (rASGeometry.TreeView.SelectedNode == null)
				{
					rASGeometry.TreeView.SelectedNode = rASGeometry;
				}
			}
			rasMapper.DisablePlotLayers = false;
			GC.Collect();
		}
		SharedData.RasMapperIsLoading = false;
	}

	public static void Get2DFlowArea(int progressX, int progressY, string geomFilename, string geomTitle, string meshname, string meshDate, ref bool SIUnits, ref int perimeterCount, ref double[] perimeterX, ref double[] perimeterY, ref int[] breaklinesStartIndex, ref double[] breaklinesX, ref double[] breaklinesY, ref int cellCount, ref double[] cellX, ref double[] cellY, ref int[] cellFacesCount, ref int[] cellFacesStartIndex, ref int[] cellFaces, ref int facePointCount, ref double[] facePointX, ref double[] facePointY, ref bool[] facePointIsPerimeter, ref int faceCount, ref int[] faceFPA, ref int[] faceFPB, ref int[] faceCellA, ref int[] faceCellB, ref bool[] faceIsPerimeter, ref int[] faceInternalStartIndex, ref int[] faceInternalCount, ref double[] faceInternalX, ref double[] faceInternalY, ref int cellMaximumIndex, ref float cellMaximumSize, ref float cellMinimumSize, ref float cellAverageSize, ref int status, ref int BadPointCount, ref int[] BadPoint, ref string statusMessage)
	{
		checked
		{
			try
			{
				SharedData.ClearAllMeshCaches();
				if (SharedData.Allow64BitHijack && !Environment.Is64BitProcess && cellCount > 5500)
				{
					MeshInputs meshInputs = new MeshInputs();
					meshInputs.ProgXLocation = progressX;
					meshInputs.ProgYLocation = progressY;
					meshInputs.GeomFilename = geomFilename;
					meshInputs.GeomTitle = geomTitle;
					meshInputs.MeshName = meshname;
					meshInputs.MeshDate = meshDate;
					meshInputs.IsSIUnits = SIUnits;
					meshInputs.PerimeterX = perimeterX;
					meshInputs.PerimeterY = perimeterY;
					meshInputs.BreaklinesStartIndex = breaklinesStartIndex;
					meshInputs.BreaklineX = breaklinesX;
					meshInputs.BreaklineY = breaklinesY;
					meshInputs.CellCount = cellCount;
					meshInputs.CellX = cellX;
					meshInputs.CellY = cellY;
					MeshOutputs meshOutputs = GenerateMesh.Launch(Guid.NewGuid().ToString(), meshInputs);
					cellFacesCount = meshOutputs.CellFacesCount;
					cellFacesStartIndex = meshOutputs.CellFacesStartIndex;
					cellFaces = meshOutputs.CellFaces;
					faceFPA = meshOutputs.FaceFPA;
					faceFPB = meshOutputs.FaceFPB;
					faceCount = faceFPA.Count();
					facePointX = meshOutputs.FacePointX;
					facePointY = meshOutputs.FacePointY;
					facePointCount = facePointX.Length;
					facePointIsPerimeter = meshOutputs.FacePointIsPerim;
					faceCellA = meshOutputs.FaceCellA;
					faceCellB = meshOutputs.FaceCellB;
					faceIsPerimeter = meshOutputs.FaceIsPerimeter;
					faceInternalStartIndex = meshOutputs.FaceInternalStartIndex;
					faceInternalCount = meshOutputs.FaceInternalCount;
					faceInternalX = meshOutputs.FaceInternalX;
					faceInternalY = meshOutputs.FaceInternalY;
					cellMaximumIndex = meshOutputs.CellMaxIndex;
					cellMaximumSize = meshOutputs.CellMaxSize;
					cellMinimumSize = meshOutputs.CellMinSize;
					cellAverageSize = meshOutputs.CellAvgSize;
					status = meshOutputs.Status;
					BadPointCount = meshOutputs.BadPtCount;
					BadPoint = meshOutputs.BadPoints;
					statusMessage = meshOutputs.StatusMsg;
					return;
				}
				System.Drawing.Point location = new System.Drawing.Point(progressX, progressY);
				if (progressX < 0)
				{
					_ = 0;
				}
				facePointCount = 0;
				faceCount = 0;
				BadPointCount = 0;
				cellMaximumIndex = -1;
				cellMaximumSize = 0f;
				cellMinimumSize = 0f;
				cellAverageSize = 0f;
				MeshFV2D meshFV2D = null;
				string geomFilenameTerm = geomFilename + ".hdf";
				if (!string.IsNullOrEmpty(geomFilename) && File.Exists(geomFilenameTerm))
				{
					using H5Reader hr = new H5Reader(geomFilenameTerm);
					if (MeshFV2D.ExistsAndIsCurrent(hr, meshname, meshDate))
					{
						meshFV2D = new MeshFV2D(hr, meshname);
					}
				}
				if (meshFV2D == null)
				{
					meshFV2D = CreateMesh(SIUnits, location, meshname, perimeterCount, perimeterX, perimeterY, breaklinesStartIndex, ref breaklinesX, ref breaklinesY, cellCount, cellX, cellY);
					if (!string.IsNullOrEmpty(geomFilename))
					{
						if (meshFV2D != null && meshFV2D.MeshCompletionState == MeshStatus.Complete)
						{
							using H5Writer hw = new H5Writer(geomFilenameTerm);
							meshFV2D.SaveMesh(hw, meshname, meshDate);
						}
						else
						{
							using H5Writer h5Writer = new H5Writer(geomFilenameTerm);
							string meshGroupName = MeshFV2D.MakeMeshGroupName(meshname);
							if (h5Writer.ExistsAsGroup(meshGroupName))
							{
								h5Writer.Delete(meshGroupName);
							}
						}
					}
				}
				if (meshFV2D == null)
				{
					facePointCount = 0;
					faceCount = 0;
					status = -1;
					statusMessage = "Unknown Mesh Error - Mesh was not created.";
					return;
				}
				cellMaximumIndex = meshFV2D.CellMaximumIndex;
				cellMaximumSize = (float)meshFV2D.CellMaximumSize;
				cellMinimumSize = (float)meshFV2D.CellMinimumSize;
				cellAverageSize = (float)meshFV2D.CellAverageSize;
				if (meshFV2D.FatalPerimeterError)
				{
					statusMessage = "FATAL ERROR: " + meshFV2D.StatusMessage;
				}
				else
				{
					statusMessage = meshFV2D.StatusMessage;
				}
				bool condition = false;
				if (meshFV2D.MeshCompletionState == MeshStatus.Complete)
				{
					status = 0;
					condition = true;
				}
				else
				{
					BadPointCount = meshFV2D.BadIndexes.Count;
					BadPoint = meshFV2D.BadIndexes.ToArray();
					if (meshFV2D.MeshCompletionState == MeshStatus.PointsOutsidePerimeter)
					{
						status = 1;
						if (Operators.CompareString(statusMessage, "", TextCompare: false) == 0)
						{
							statusMessage = "Points outside Perimeter";
						}
					}
					else if (meshFV2D.MeshCompletionState == MeshStatus.DuplicatePoints)
					{
						status = 2;
					}
					else if (meshFV2D.MeshCompletionState == MeshStatus.DisconnectedGraphs)
					{
						status = 3;
						condition = true;
					}
					else if (meshFV2D.MeshCompletionState == MeshStatus.FacePerimeterConnectionError)
					{
						status = 4;
						if (!meshFV2D.FatalPerimeterError)
						{
							condition = true;
						}
					}
					else if (meshFV2D.MeshCompletionState == MeshStatus.MaxFacesPerCellExceeded)
					{
						status = 5;
						condition = true;
					}
					else if (meshFV2D.MeshCompletionState == MeshStatus.MaxFacesPerFacepointExceeded)
					{
						status = 5;
						condition = true;
					}
					else if (meshFV2D.MeshCompletionState == MeshStatus.FewerThanMinPerimeterPoints)
					{
						status = 6;
					}
					else if (meshFV2D.MeshCompletionState == MeshStatus.FewerThanMinNumCells)
					{
						status = 7;
					}
					else if (meshFV2D.MeshCompletionState == MeshStatus.CellAreaTotalError)
					{
						status = 8;
						condition = true;
					}
					else if (meshFV2D.MeshCompletionState == MeshStatus.MinFaceLenError)
					{
						status = -1;
						condition = true;
					}
					else
					{
						status = -1;
					}
				}
				if (condition)
				{
					Stopwatch stopwatch = Stopwatch.StartNew();
					int cellFacesStartIndex2 = 0;
					int count = cellCount - 1;
					for (int i = 0; i <= count; i++)
					{
						cellFacesStartIndex2 += meshFV2D.CellFacesCount(i);
					}
					cellFacesCount = new int[cellCount - 1 + 1];
					cellFacesStartIndex = new int[cellCount - 1 + 1];
					cellFaces = new int[cellFacesStartIndex2 - 1 + 1];
					cellFacesStartIndex2 = 0;
					int count2 = cellCount - 1;
					for (int j = 0; j <= count2; j++)
					{
						int[] cellFacesArray = meshFV2D.CellFaces(j);
						if (cellFacesArray.Count() > 0)
						{
							cellFacesCount[j] = cellFacesArray.Count();
							cellFacesStartIndex[j] = cellFacesStartIndex2;
							Array.Copy(cellFacesArray, 0, cellFaces, cellFacesStartIndex2, cellFacesArray.Count());
							cellFacesStartIndex2 += cellFacesArray.Count();
						}
					}
					facePointCount = meshFV2D.FacePointCount;
					facePointX = new double[facePointCount - 1 + 1];
					facePointY = new double[facePointCount - 1 + 1];
					facePointIsPerimeter = new bool[facePointCount - 1 + 1];
					int count3 = facePointCount - 1;
					for (int k = 0; k <= count3; k++)
					{
						meshFV2D.FacePointXY(k, ref facePointX[k], ref facePointY[k], ref facePointIsPerimeter[k]);
					}
					faceCount = meshFV2D.FaceCount;
					faceFPA = new int[faceCount - 1 + 1];
					faceFPB = new int[faceCount - 1 + 1];
					faceCellA = new int[faceCount - 1 + 1];
					faceCellB = new int[faceCount - 1 + 1];
					faceIsPerimeter = new bool[faceCount - 1 + 1];
					int internalPointCountSum = 0;
					int count4 = faceCount - 1;
					for (int l = 0; l <= count4; l++)
					{
						Face face = meshFV2D.Faces[l];
						faceFPA[l] = face.fpA;
						faceFPB[l] = face.fpB;
						faceCellA[l] = face.cellA;
						faceCellB[l] = face.cellB;
						faceIsPerimeter[l] = meshFV2D.FaceIsPerimeter(l);
						if (face.InternalPoints != null)
						{
							internalPointCountSum += face.InternalPoints.Count;
						}
					}
					faceInternalStartIndex = new int[faceCount - 1 + 1];
					faceInternalCount = new int[faceCount - 1 + 1];
					faceInternalX = new double[internalPointCountSum - 1 + 1];
					faceInternalY = new double[internalPointCountSum - 1 + 1];
					internalPointCountSum = 0;
					int count5 = faceCount - 1;
					for (int m = 0; m <= count5; m++)
					{
						Face face2 = meshFV2D.Faces[m];
						if (face2.InternalPoints != null)
						{
							faceInternalStartIndex[m] = internalPointCountSum;
							faceInternalCount[m] = face2.InternalPoints.Count;
							int count6 = faceInternalCount[m] - 1;
							for (int n = 0; n <= count6; n++)
							{
								faceInternalX[internalPointCountSum] = face2.InternalPoints[n].X;
								faceInternalY[internalPointCountSum] = face2.InternalPoints[n].Y;
								internalPointCountSum++;
							}
						}
					}
					stopwatch.Stop();
				}
				else
				{
					cellFacesCount = null;
					cellFacesStartIndex = null;
					cellFaces = null;
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				facePointCount = 0;
				faceCount = 0;
				BadPointCount = 0;
				status = -1;
				statusMessage = statusMessage + "  Exception thrown during mesh creation = " + ex2.Message;
				statusMessage = statusMessage + "  Stack Trace = " + ex2.StackTrace;
				ProjectData.ClearProjectError();
			}
		}
	}

	private static MeshFV2D CreateMesh(bool SIUnits, System.Drawing.Point location, string name, int perimeterCount, double[] perimeterX, double[] perimeterY, int[] breaklinesStartIndex, ref double[] breaklinesX, ref double[] breaklinesY, int cellCount, double[] cellX, double[] cellY)
	{
		if (SIUnits)
		{
			SharedData.SetUnitsSystem(SharedData.EnumUnits.SIUnits);
		}
		else
		{
			SharedData.SetUnitsSystem(SharedData.EnumUnits.USCustomary);
		}
		PointMs pointMs = new PointMs();
		pointMs.AddRange(perimeterCount, perimeterX, perimeterY);
		Polygon perimeter = new Polygon(pointMs);
		Polyline breakline = null;
		if (breaklinesX.Count() > 1)
		{
			PointMs pointMs2 = new PointMs();
			pointMs2.AddRange(breaklinesX.Length, breaklinesX, breaklinesY);
			breakline = new Polyline(pointMs2, breaklinesStartIndex);
		}
		VirtualPointMs cellCenters = new VirtualPointMs(cellCount, cellX, cellY);
		if (location.X >= 0)
		{
			_ = location.Y;
			_ = 0;
		}
		return new MeshFV2D(perimeter, cellCenters, breakline);
	}

	public static TerrainLayer GetTerrainFromXML(XmlDocument doc, string layername)
	{
		XmlNode xmlNode = doc.SelectSingleNode("RASMapper/Terrains/Layer[@Name='" + layername + "']");
		if (xmlNode == null)
		{
			return null;
		}
		if (Operators.CompareString(XML.StringAttribute(xmlNode, "Type"), "TerrainLayer", TextCompare: false) == 0)
		{
			TerrainLayer terrainLayer = new TerrainLayer(layername);
			terrainLayer.XMLLoad((XmlElement)xmlNode);
			return terrainLayer;
		}
		return null;
	}

	public static string GetSRSFromRasmapDoc(XmlDocument doc, string baseFilename)
	{
		XmlNode sRSXMLNode = GetSRSXMLNode(doc);
		if (sRSXMLNode == null)
		{
			return "";
		}
		return RasMapperLib.Utilities.Path.MakeAbsolute(XML.StringAttribute(sRSXMLNode, "Filename"), baseFilename);
	}

	private static XmlNode GetSRSXMLNode(XmlDocument doc)
	{
		return doc?.SelectSingleNode("RASMapper")?.SelectSingleNode("RASProjectionFilename");
	}

	public static FeatureLayer GetMapLayerFromXML(XmlDocument doc, string layername)
	{
		if (doc == null)
		{
			return null;
		}
		XmlNode xmlNode = doc.SelectSingleNode("RASMapper/MapLayers/Layer[@Name='" + layername + "']");
		if (xmlNode == null)
		{
			return null;
		}
		XML.StringAttribute(xmlNode, "Type");
		Layer.XMLLoadFilename(xmlNode);
		throw new Exception("Rasmap file needs to be well known for this function to work.");
	}

	public void ImportRasterFilesAndSubDataSets(ref string[] candidateFiles, ref int fileCount, ref string[] filenames, bool BaseDatasetsExist, ref int subDatasetCount, ref string[] SubDatasets, ProgressReporter prog)
	{
		BaseDatasetsExist = false;
		List<string> stringList = new List<string>();
		List<string> stringListList = new List<string>();
		string[] candidateFilesArray = candidateFiles;
		checked
		{
			int count = candidateFilesArray.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				try
				{
					string candidateFile = candidateFilesArray[i];
					using GDALRaster gDALRaster = new GDALRaster(candidateFile);
					Dictionary<string, string> subDatasets = gDALRaster.GetSubDatasets();
					if (subDatasets.Count > 0)
					{
						stringList.Add(candidateFile);
						foreach (string key in subDatasets.Keys)
						{
							string gdalSubDatasetName = GDALSubDatasetName(key);
							if (!stringListList.Contains(gdalSubDatasetName, StringComparer.CurrentCultureIgnoreCase))
							{
								stringListList.Add(gdalSubDatasetName);
							}
						}
						if (!BaseDatasetsExist)
						{
							BaseDatasetsExist = gDALRaster.GetBandCount() > 0;
						}
					}
					else if (gDALRaster.GetBandCount() > 0)
					{
						BaseDatasetsExist = true;
						stringList.Add(candidateFile);
					}
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					ProjectData.ClearProjectError();
				}
				prog.ReportProgress((int)System.Math.Round(100f * (float)((double)(i + 1) / (double)candidateFilesArray.Count())));
			}
			fileCount = stringList.Count;
			filenames = stringList.ToArray();
			stringListList.Sort(StringComparer.CurrentCultureIgnoreCase);
			subDatasetCount = stringListList.Count;
			SubDatasets = stringListList.ToArray();
		}
	}

	private string GDALSubDatasetName(string subDSKey)
	{
		return subDSKey.Split(':').LastOrDefault();
	}

	public static string[] TerrainFilesAvailable()
	{
		List<string> stringList = new List<string>();
		XmlDocument rasMapDoc = SharedData.RasMapDoc;
		if (rasMapDoc != null && rasMapDoc.DocumentElement != null)
		{
			XmlNodeList xmlNodeList = rasMapDoc.DocumentElement.SelectNodes("Terrains/Layer");
			foreach (object xmlNodeList2 in xmlNodeList)
			{
				string stringAttribute = XML.StringAttribute((IXPathNavigable)RuntimeHelpers.GetObjectValue(xmlNodeList2), "Filename");
				if (Operators.CompareString(stringAttribute, "", TextCompare: false) != 0)
				{
					string absolute = RasMapperLib.Utilities.Path.MakeAbsolute(stringAttribute, SharedData.RasMapFilename);
					stringList.Add(absolute);
				}
			}
		}
		return stringList.ToArray();
	}

	public static bool H5_CopyGeometryGroup(string sourceFilename, string destinationFilename)
	{
		return H5_CopyGeometryBase(sourceFilename, destinationFilename, copyDotNetOnly: false);
	}

	public static bool H5_CopyGeometryH5OnlyData(string sourceFilename, string destinationFilename)
	{
		return H5_CopyGeometryBase(sourceFilename, destinationFilename, copyDotNetOnly: true);
	}

	private static bool H5_CopyGeometryBase(string sourceFilename, string destinationFilename, bool copyDotNetOnly)
	{
		bool result;
		try
		{
			using (H5Reader h5Reader = new H5Reader(sourceFilename))
			{
				using H5Writer h5Writer = new H5Writer(destinationFilename);
				if (copyDotNetOnly)
				{
					h5Writer.CreateGroup("Geometry");
					h5Reader.CopyAttributesTo(h5Writer, "Geometry", "Geometry");
					MeshRegionLayer.Migrate(h5Reader, h5Writer);
					RASPipeLinks.Migrate(h5Reader, h5Writer);
					RASPipeNodes.Migrate(h5Reader, h5Writer);
				}
				else
				{
					h5Reader.CopyAttributesTo(h5Writer, "/", "/");
					h5Reader.CopyTo(h5Writer, "Geometry", "Geometry");
				}
				string[] lookupTable = new string[7] { "Ras.exe Date", "RasMapperLib.dll Date", "RasUnsteady64.exe Date", "RasSteady64.exe Date", "RasSediment64.exe Date", "RasGeomPreprocess64.exe Date", "RasUnsteadySediment64.exe Date" };
				foreach (string attributeName in lookupTable)
				{
					if (h5Writer.ExistsAsAttribute("Geometry", attributeName))
					{
						h5Writer.DeleteAttribute("Geometry", attributeName);
					}
				}
			}
			if (!System.IO.Path.GetDirectoryName(sourceFilename).Equals(System.IO.Path.GetDirectoryName(destinationFilename), StringComparison.OrdinalIgnoreCase))
			{
				RASGeometry.UpdateAssociatedFilenames(sourceFilename, destinationFilename);
			}
			result = true;
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = false;
			ProjectData.ClearProjectError();
		}
		return result;
	}
}
