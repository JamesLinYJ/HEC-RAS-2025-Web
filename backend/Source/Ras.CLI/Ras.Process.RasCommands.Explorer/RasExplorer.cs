using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Classifiers;
using Ras.Layers.Results;
using Ras.Layers.Terrains;
using Ras.Process.RasCommands.Explorer.Actions;
using Ras.Process.RasCommands.Explorer.FeatureTable;
using Ras.Process.RasCommands.Explorer.GeometryEditor;
using Ras.Process.RasCommands.Explorer.PlanEditor;
using Ras.Process.RasCommands.Explorer.SubLayers;
using Spectre.Console;
using Spectre.Console.Rendering;
using Ras.Process.Platform;

namespace Ras.Process.RasCommands.Explorer;

public class RasExplorer
{
	private class AssociationChoice
	{
		public object Value { get; set; }

		public string DisplayName { get; set; }

		public bool IsCancel { get; set; }

		public bool IsNone { get; set; }
	}

	private class EnumChoice
	{
		public object Value { get; set; }

		public string DisplayName { get; set; }

		public bool IsCancel { get; set; }
	}

	private Project _project;

	private readonly string _filename;

	private readonly RasExplorerState _state = new RasExplorerState();

	private readonly RasTreeRenderer _treeRenderer = new RasTreeRenderer();

	private readonly FeatureTableRenderer _featureTableRenderer = new FeatureTableRenderer();

	private readonly PlanEditorRenderer _planEditorRenderer = new PlanEditorRenderer();

	private readonly GeometryEditorRenderer _geometryEditorRenderer = new GeometryEditorRenderer();

	private readonly ActionRegistry _actionRegistry = ActionRegistry.CreateDefault();

	private object _featureTableLayer;

	private Geometry _featureTableParentGeometry;

	private string _lastSelectedPath;

	private const string CancelChoice = "[dim]< Cancel >[/]";

	private const string NoneChoice = "[dim]< None >[/]";

	public RasExplorer(string filename)
	{
		_filename = filename;
	}

	public bool Run()
	{
		Console.CursorVisible = false;
		try
		{
			AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("cyan"))
				.Start("Loading " + Path.GetFileName(_filename) + "...", (StatusContext ctx) =>
				{
					ctx.Status("Initializing GDAL...");
					GDALSetup.InitializeMultiplatform();
					ctx.Status("Opening project file...");
					_project = new Project(_filename);
					ctx.Status("Loading project structure...");
					InitializeTree();
				});
			AnsiConsole.Clear();
			bool keepRunning = true;
			while (keepRunning)
			{
				AnsiConsole.Live(Render()).AutoClear(enabled: false).Start((LiveDisplayContext ctx) =>
				{
					ctx.Refresh();
					while (true)
					{
						ConsoleKeyInfo key = Console.ReadKey(intercept: true);
						if (!ProcessInput(key))
						{
							keepRunning = false;
							break;
						}
						PlanEditorState planEditorState2 = _state.PlanEditorState;
						if (planEditorState2 != null && planEditorState2.NeedsSelectionPrompt)
						{
							break;
						}
						GeometryEditorState geometryEditorState2 = _state.GeometryEditorState;
						if (geometryEditorState2 != null && geometryEditorState2.NeedsSelectionPrompt)
						{
							break;
						}
						ctx.UpdateTarget(Render());
						ctx.Refresh();
					}
				});
				PlanEditorState planEditorState = _state.PlanEditorState;
				if (planEditorState != null && planEditorState.NeedsSelectionPrompt)
				{
					ShowPlanEditorSelectionPrompt();
					keepRunning = true;
					AnsiConsole.Clear();
				}
				GeometryEditorState geometryEditorState = _state.GeometryEditorState;
				if (geometryEditorState != null && geometryEditorState.NeedsSelectionPrompt)
				{
					ShowGeometryEditorSelectionPrompt();
					keepRunning = true;
					AnsiConsole.Clear();
				}
			}
			return true;
		}
		finally
		{
			Console.CursorVisible = true;
			AnsiConsole.Clear();
		}
	}

	private IRenderable Render()
	{
		int windowHeight = Console.WindowHeight;
		int windowWidth = Console.WindowWidth;
		return _state.Mode switch
		{
			ExplorerMode.Tree => _treeRenderer.Render(_state, _project.Name, windowWidth, windowHeight), 
			ExplorerMode.FeatureTable => _featureTableRenderer.Render(_state.FeatureTableState, windowWidth, windowHeight), 
			ExplorerMode.PlanEditor => _planEditorRenderer.Render(_state.PlanEditorState, windowWidth, windowHeight), 
			ExplorerMode.GeometryEditor => _geometryEditorRenderer.Render(_state.GeometryEditorState, windowWidth, windowHeight), 
			_ => _treeRenderer.Render(_state, _project.Name, windowWidth, windowHeight), 
		};
	}

	private void InitializeTree()
	{
		_state.Root = new RasTreeNode(_project.Name, "/", RasNodeType.Root)
		{
			DataSource = _project,
			IsExpanded = true
		};
		LoadProjectCollections(_state.Root);
		foreach (RasTreeNode child in _state.Root.Children)
		{
			child.IsExpanded = true;
			LoadCollectionItems(child);
		}
		_state.RefreshVisibleNodes();
		UpdateSelectedNodeInfo();
	}

	private void LoadProjectCollections(RasTreeNode root)
	{
		AddCollection(root, "Plans", _project.Plans.Count);
		AddCollection(root, "Geometries", _project.Geometries.Count);
		AddCollection(root, "BoundaryConditions", _project.BoundaryConditions.Count);
		AddCollection(root, "Results", _project.Results.Count);
		AddCollection(root, "Terrains", _project.Terrains.Count);
		AddCollection(root, "NValueLayers", _project.NValues.Count);
		AddCollection(root, "ClassificationLayers", _project.ClassificationLayers.Count);
	}

	private void AddCollection(RasTreeNode root, string name, int count)
	{
		RasTreeNode rasTreeNode = new RasTreeNode(name, "/" + name, RasNodeType.Collection, root)
		{
			DisplaySuffix = $"[{count}]"
		};
		root.Children.Add(rasTreeNode);
	}

	private void LoadCollectionItems(RasTreeNode collectionNode)
	{
		if (collectionNode.ChildrenLoaded)
		{
			return;
		}
		switch (collectionNode.Name)
		{
		case "Plans":
			foreach (Plan plan in _project.Plans)
			{
				string displaySuffix6 = BuildPlanSuffix(_project.GetPlanAssociation(plan));
				RasTreeNode rasTreeNode9 = new RasTreeNode(plan.Name, "/Plans/" + plan.Name, RasNodeType.Plan, collectionNode)
				{
					DataSource = plan,
					DisplaySuffix = displaySuffix6
				};
				collectionNode.Children.Add(rasTreeNode9);
			}
			break;
		case "Geometries":
			foreach (Geometry geometry in _project.Geometries)
			{
				string displaySuffix5 = BuildGeometrySuffix(_project.GetGeometryAssociation(geometry));
				RasTreeNode rasTreeNode8 = new RasTreeNode(geometry.Name, "/Geometries/" + geometry.Name, RasNodeType.Geometry, collectionNode)
				{
					DataSource = geometry,
					DisplaySuffix = displaySuffix5
				};
				collectionNode.Children.Add(rasTreeNode8);
				foreach (RasTreeNode subLayer in GeometrySubLayerProvider.GetSubLayers(geometry, rasTreeNode8))
				{
					rasTreeNode8.Children.Add(subLayer);
				}
				rasTreeNode8.ChildrenLoaded = true;
			}
			break;
		case "BoundaryConditions":
			foreach (BoundaryCondition boundaryCondition in _project.BoundaryConditions)
			{
				string displaySuffix4 = $"[{boundaryCondition.StartTime:yyyy-MM-dd} - {boundaryCondition.EndTime:yyyy-MM-dd}]";
				RasTreeNode rasTreeNode7 = new RasTreeNode(boundaryCondition.Name, "/BoundaryConditions/" + boundaryCondition.Name, RasNodeType.BoundaryCondition, collectionNode)
				{
					DataSource = boundaryCondition,
					DisplaySuffix = displaySuffix4
				};
				collectionNode.Children.Add(rasTreeNode7);
				foreach (RasTreeNode subLayer2 in BoundaryConditionSubLayerProvider.GetSubLayers(boundaryCondition, rasTreeNode7))
				{
					rasTreeNode7.Children.Add(subLayer2);
				}
				rasTreeNode7.ChildrenLoaded = true;
			}
			break;
		case "Results":
			foreach (Result result in _project.Results)
			{
				(string plain, string markup) tuple = BuildResultSuffix(result);
				string plain = tuple.plain;
				string markup = tuple.markup;
				RasTreeNode rasTreeNode4 = new RasTreeNode(result.Name, "/Results/" + result.Name, RasNodeType.Result, collectionNode)
				{
					DataSource = result,
					DisplaySuffix = plain,
					SuffixMarkup = markup
				};
				collectionNode.Children.Add(rasTreeNode4);
				if (result.Plan != null)
				{
					RasTreeNode rasTreeNode10 = new RasTreeNode(result.Plan.Name, rasTreeNode4.FullPath + "/Plan", RasNodeType.Plan, rasTreeNode4)
					{
						DataSource = result.Plan,
						DisplaySuffix = "[Embedded]"
					};
					rasTreeNode4.Children.Add(rasTreeNode10);
				}
				if (result.Geometry != null)
				{
					RasTreeNode rasTreeNode5 = new RasTreeNode(result.Geometry.Name, rasTreeNode4.FullPath + "/Geometry", RasNodeType.Geometry, rasTreeNode4)
					{
						DataSource = result.Geometry,
						DisplaySuffix = "[Embedded]"
					};
					rasTreeNode4.Children.Add(rasTreeNode5);
					foreach (RasTreeNode subLayer3 in GeometrySubLayerProvider.GetSubLayers(result.Geometry, rasTreeNode5))
					{
						rasTreeNode5.Children.Add(subLayer3);
					}
					rasTreeNode5.ChildrenLoaded = true;
				}
				if (result.BoundaryConditions != null)
				{
					RasTreeNode rasTreeNode6 = new RasTreeNode(result.BoundaryConditions.Name, rasTreeNode4.FullPath + "/BoundaryConditions", RasNodeType.BoundaryCondition, rasTreeNode4)
					{
						DataSource = result.BoundaryConditions,
						DisplaySuffix = "[Embedded]"
					};
					rasTreeNode4.Children.Add(rasTreeNode6);
					foreach (RasTreeNode subLayer4 in BoundaryConditionSubLayerProvider.GetSubLayers(result.BoundaryConditions, rasTreeNode6))
					{
						rasTreeNode6.Children.Add(subLayer4);
					}
					rasTreeNode6.ChildrenLoaded = true;
				}
				rasTreeNode4.ChildrenLoaded = true;
			}
			break;
		case "Terrains":
			foreach (Terrain terrain in _project.Terrains)
			{
				string displaySuffix3 = $"[{terrain.LayerCount} layers]";
				RasTreeNode rasTreeNode3 = new RasTreeNode(terrain.Name, "/Terrains/" + terrain.Name, RasNodeType.Terrain, collectionNode)
				{
					DataSource = terrain,
					DisplaySuffix = displaySuffix3
				};
				collectionNode.Children.Add(rasTreeNode3);
				foreach (RasTreeNode subLayer5 in TerrainSubLayerProvider.GetSubLayers(terrain, rasTreeNode3))
				{
					rasTreeNode3.Children.Add(subLayer5);
				}
				rasTreeNode3.ChildrenLoaded = true;
			}
			break;
		case "NValueLayers":
			foreach (NValueLayer nValue in _project.NValues)
			{
				string displaySuffix2 = $"[{nValue.Mode}]";
				RasTreeNode rasTreeNode2 = new RasTreeNode(nValue.Name, "/NValueLayers/" + nValue.Name, RasNodeType.NValue, collectionNode)
				{
					DataSource = nValue,
					DisplaySuffix = displaySuffix2
				};
				collectionNode.Children.Add(rasTreeNode2);
				foreach (RasTreeNode subLayer6 in NValueSubLayerProvider.GetSubLayers(nValue, rasTreeNode2))
				{
					rasTreeNode2.Children.Add(subLayer6);
				}
				rasTreeNode2.ChildrenLoaded = true;
			}
			break;
		case "ClassificationLayers":
			foreach (ClassificationLayer classificationLayer in _project.ClassificationLayers)
			{
				string displaySuffix = $"[{classificationLayer.LayerCount} layers]";
				RasTreeNode rasTreeNode = new RasTreeNode(classificationLayer.Name, "/ClassificationLayers/" + classificationLayer.Name, RasNodeType.Classification, collectionNode)
				{
					DataSource = classificationLayer,
					DisplaySuffix = displaySuffix
				};
				collectionNode.Children.Add(rasTreeNode);
				foreach (RasTreeNode subLayer7 in ClassificationSubLayerProvider.GetSubLayers(classificationLayer, rasTreeNode))
				{
					rasTreeNode.Children.Add(subLayer7);
				}
				rasTreeNode.ChildrenLoaded = true;
			}
			break;
		}
		collectionNode.ChildrenLoaded = true;
	}

	private static string BuildPlanSuffix(PlanAssociation assoc)
	{
		if (assoc == null)
		{
			return "";
		}
		List<string> stringList = new List<string>();
		if (assoc.Geometry != null)
		{
			stringList.Add("Geom: " + assoc.Geometry.Name);
		}
		if (assoc.BoundaryCondition != null)
		{
			stringList.Add("BC: " + assoc.BoundaryCondition.Name);
		}
		if (stringList.Count <= 0)
		{
			return "";
		}
		return "-> " + string.Join(", ", stringList);
	}

	private static string BuildGeometrySuffix(GeometryAssociation assoc)
	{
		if (assoc == null)
		{
			return "";
		}
		List<string> stringList = new List<string>();
		if (assoc.Terrain != null)
		{
			stringList.Add("Terr: " + assoc.Terrain.Name);
		}
		if (assoc.NValueLayer != null)
		{
			stringList.Add("NVal: " + assoc.NValueLayer.Name);
		}
		if (stringList.Count <= 0)
		{
			return "";
		}
		return "-> " + string.Join(", ", stringList);
	}

	private static (string plain, string markup) BuildResultSuffix(Result res)
	{
		ComputeSummary computeSummary = res.ComputeSummary;
		if (computeSummary == null)
		{
			return (plain: "", markup: "");
		}
		List<string> stringList = new List<string>();
		List<string> stringListList = new List<string>();
		if (computeSummary.TotalDuration != default(TimeSpan))
		{
			TimeSpan totalDuration = computeSummary.TotalDuration;
			string totalDurationTerm;
			if (totalDuration.TotalHours >= 1.0)
			{
				totalDurationTerm = totalDuration.ToString("h\\:mm\\:ss");
			}
			else
			{
				totalDurationTerm = ((!(totalDuration.TotalMinutes >= 1.0)) ? $"{totalDuration.TotalSeconds:F1}s" : totalDuration.ToString("m\\:ss"));
			}
			stringList.Add(totalDurationTerm);
			stringListList.Add("[dim]" + totalDurationTerm + "[/]");
		}
		if (computeSummary.Success)
		{
			stringList.Add("Success");
			stringListList.Add("[green]Success[/]");
		}
		else
		{
			string failureReason = (string.IsNullOrEmpty(computeSummary.FailureReason) ? "Failed" : computeSummary.FailureReason);
			stringList.Add(failureReason);
			stringListList.Add("[red]" + failureReason + "[/]");
		}
		return (plain: string.Join(" ", stringList), markup: string.Join(" ", stringListList));
	}

	private void UpdateSelectedNodeInfo()
	{
		RasTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null)
		{
			_state.SelectedNodeInfo = null;
			_state.FocusOnInfoPanel = false;
			_lastSelectedPath = null;
			return;
		}
		if (_lastSelectedPath != selectedNode.FullPath)
		{
			_state.SelectedPropertyIndex = 0;
			_state.InfoPanelScrollOffset = 0;
			_state.FocusOnInfoPanel = false;
			_lastSelectedPath = selectedNode.FullPath;
		}
		RasNodeInfoData rasNodeInfoData = new RasNodeInfoData();
		object dataSource = selectedNode.DataSource;
		if (!(dataSource is Project project))
		{
			if (!(dataSource is Plan plan))
			{
				if (!(dataSource is Geometry geometry))
				{
					if (!(dataSource is BoundaryCondition boundaryCondition))
					{
						if (!(dataSource is Result result))
						{
							if (!(dataSource is Terrain terrain))
							{
								if (!(dataSource is NValueLayer nValueLayer))
								{
									if (!(dataSource is ClassificationLayer classificationLayer))
									{
										if (!(dataSource is TopologyLayer topologyLayer))
										{
											if (!(dataSource is FlowAreaLayer flowAreaLayer))
											{
												if (!(dataSource is CulvertBarrelLayer culvertBarrelLayer))
												{
													if (!(dataSource is OpeningPropertiesLayer openingPropertiesLayer))
													{
														if (!(dataSource is BarrelPropertiesLayer barrelPropertiesLayer))
														{
															if (!(dataSource is BCLinesWrapper bCLinesWrapper))
															{
																if (!(dataSource is BoundaryConditionLineLayer boundaryConditionLineLayer))
																{
																	if (!(dataSource is PrecipitationLayer precipitationLayer))
																	{
																		if (!(dataSource is WindLayer windLayer))
																		{
																			if (!(dataSource is SpatialBoundaryConditionBase spatialBoundaryConditionBase))
																			{
																				if (!(dataSource is MeteorologicStationLayer meteorologicStationLayer))
																				{
																					if (!(dataSource is MeshPropertyTableSignature meshPropertyTableSignature))
																					{
																						if (!(dataSource is RasterTerrainInput rasterTerrainInput))
																						{
																							if (!(dataSource is ITerrainInput terrainInput))
																							{
																								if (!(dataSource is RasterClassificationInput rasterClassificationInput))
																								{
																									if (!(dataSource is PolygonClassificationInput polygonClassificationInput))
																									{
																										if (!(dataSource is IClassificationInput classificationInput))
																										{
																											if (dataSource is NVTable nVTable)
																											{
																												rasNodeInfoData.Name = "Interpretation Table";
																												rasNodeInfoData.Properties["Entry Count"] = nVTable.Count;
																												foreach (KeyValuePair<string, float> rawDatum in nVTable.RawData)
																												{
																													rasNodeInfoData.Properties["  " + rawDatum.Key] = rawDatum.Value;
																												}
																											}
																											else if (selectedNode.Type == RasNodeType.Collection)
																											{
																												rasNodeInfoData.Name = selectedNode.Name;
																												switch (selectedNode.Name)
																												{
																												case "Plans":
																													rasNodeInfoData.Properties["Count"] = _project.Plans.Count;
																													rasNodeInfoData.Table = new InfoTable();
																													rasNodeInfoData.Table.AddColumn("Name");
																													rasNodeInfoData.Table.AddColumn("Start");
																													rasNodeInfoData.Table.AddColumn("Duration");
																													rasNodeInfoData.Table.AddColumn("Geometry");
																													rasNodeInfoData.Table.AddColumn("Boundary Condition");
																													foreach (Plan plan2 in _project.Plans)
																													{
																														PlanAssociation planAssociation = _project.GetPlanAssociation(plan2);
																														rasNodeInfoData.Table.AddRow(plan2.Name, plan2.StartTime.ToString("yyyy-MM-dd"), (plan2.EndTime - plan2.StartTime).ToString("d\\.hh\\:mm"), planAssociation?.Geometry?.Name ?? "-", planAssociation?.BoundaryCondition?.Name ?? "-");
																													}
																													break;
																												case "Geometries":
																													rasNodeInfoData.Properties["Count"] = _project.Geometries.Count;
																													rasNodeInfoData.Table = new InfoTable();
																													rasNodeInfoData.Table.AddColumn("Name");
																													rasNodeInfoData.Table.AddColumn("Mesh Cells");
																													rasNodeInfoData.Table.AddColumn("Mesh Faces");
																													rasNodeInfoData.Table.AddColumn("Terrain");
																													rasNodeInfoData.Table.AddColumn("N-Value");
																													foreach (Geometry geometry2 in _project.Geometries)
																													{
																														GeometryAssociation geometryAssociation = _project.GetGeometryAssociation(geometry2);
																														rasNodeInfoData.Table.AddRow(geometry2.Name, geometry2.Mesh?.CellCount.ToString() ?? "-", geometry2.Mesh?.FaceCount.ToString() ?? "-", geometryAssociation?.Terrain?.Name ?? "-", geometryAssociation?.NValueLayer?.Name ?? "-");
																													}
																													break;
																												case "Results":
																													rasNodeInfoData.Properties["Count"] = _project.Results.Count;
																													rasNodeInfoData.Table = new InfoTable();
																													rasNodeInfoData.Table.AddColumn("Name");
																													rasNodeInfoData.Table.AddColumn("Duration");
																													rasNodeInfoData.Table.AddColumn("Profiles");
																													rasNodeInfoData.Table.AddColumn("Plan");
																													rasNodeInfoData.Table.AddColumn("Geometry");
																													foreach (Result result2 in _project.Results)
																													{
																														string totalDurationComputeSummaryTerm = "-";
																														ComputeSummary computeSummary = result2.ComputeSummary;
																														if (computeSummary != null)
																														{
																															_ = computeSummary.TotalDuration;
																															if (true)
																															{
																																totalDurationComputeSummaryTerm = result2.ComputeSummary.TotalDuration.ToString("h\\:mm\\:ss");
																															}
																														}
																														rasNodeInfoData.Table.AddRow(result2.Name, totalDurationComputeSummaryTerm, result2.ProfileCount.ToString(), result2.Plan?.Name ?? "-", result2.Geometry?.Name ?? "-");
																													}
																													break;
																												case "Terrains":
																													rasNodeInfoData.Properties["Count"] = _project.Terrains.Count;
																													rasNodeInfoData.Table = new InfoTable();
																													rasNodeInfoData.Table.AddColumn("Name");
																													rasNodeInfoData.Table.AddColumn("Layers");
																													foreach (Terrain terrain2 in _project.Terrains)
																													{
																														rasNodeInfoData.Table.AddRow(terrain2.Name, terrain2.LayerCount.ToString());
																													}
																													break;
																												case "BoundaryConditions":
																													rasNodeInfoData.Properties["Count"] = _project.BoundaryConditions.Count;
																													rasNodeInfoData.Table = new InfoTable();
																													rasNodeInfoData.Table.AddColumn("Name");
																													rasNodeInfoData.Table.AddColumn("Start");
																													rasNodeInfoData.Table.AddColumn("End");
																													rasNodeInfoData.Table.AddColumn("Duration");
																													foreach (BoundaryCondition boundaryCondition2 in _project.BoundaryConditions)
																													{
																														rasNodeInfoData.Table.AddRow(boundaryCondition2.Name, boundaryCondition2.StartTime.ToString("yyyy-MM-dd HH:mm"), boundaryCondition2.EndTime.ToString("yyyy-MM-dd HH:mm"), boundaryCondition2.Duration.ToString("d\\.hh\\:mm"));
																													}
																													break;
																												case "NValueLayers":
																													rasNodeInfoData.Properties["Count"] = _project.NValues.Count;
																													rasNodeInfoData.Table = new InfoTable();
																													rasNodeInfoData.Table.AddColumn("Name");
																													rasNodeInfoData.Table.AddColumn("Mode");
																													rasNodeInfoData.Table.AddColumn("Value / Classification");
																													foreach (NValueLayer nValue in _project.NValues)
																													{
																														string constantValue = ((nValue.Mode == NValueSource.Constant) ? nValue.ConstantValue.ToString("F3") : (nValue.ClassificationLayerName ?? "-"));
																														rasNodeInfoData.Table.AddRow(nValue.Name, nValue.Mode.ToString(), constantValue);
																													}
																													break;
																												case "ClassificationLayers":
																													rasNodeInfoData.Properties["Count"] = _project.ClassificationLayers.Count;
																													rasNodeInfoData.Table = new InfoTable();
																													rasNodeInfoData.Table.AddColumn("Name");
																													rasNodeInfoData.Table.AddColumn("Layers");
																													rasNodeInfoData.Table.AddColumn("Classes");
																													foreach (ClassificationLayer classificationLayer2 in _project.ClassificationLayers)
																													{
																														rasNodeInfoData.Table.AddRow(classificationLayer2.Name, classificationLayer2.LayerCount.ToString(), classificationLayer2.ClassificationMap?.Count.ToString() ?? "-");
																													}
																													break;
																												default:
																													rasNodeInfoData.Properties["Items"] = selectedNode.DisplaySuffix;
																													break;
																												}
																											}
																											else
																											{
																												rasNodeInfoData.Name = selectedNode.Name;
																											}
																										}
																										else
																										{
																											rasNodeInfoData.Name = classificationInput.Name;
																											rasNodeInfoData.Properties["Type"] = classificationInput.TypeName;
																											classificationInput.IsValid(out var err);
																											rasNodeInfoData.Properties["Status"] = (string.IsNullOrEmpty(err) ? "Valid" : err);
																										}
																									}
																									else
																									{
																										rasNodeInfoData.Name = polygonClassificationInput.Name;
																										rasNodeInfoData.Properties["Type"] = polygonClassificationInput.TypeName;
																										Classification classifier = polygonClassificationInput.GetClassifier();
																										if (classifier != null && classifier.Count > 0)
																										{
																											rasNodeInfoData.Properties["Classes"] = classifier.Count;
																										}
																									}
																								}
																								else
																								{
																									rasNodeInfoData.Name = rasterClassificationInput.Name;
																									rasNodeInfoData.Properties["Type"] = rasterClassificationInput.TypeName;
																									rasNodeInfoData.Filename = rasterClassificationInput.Filename;
																									bool isValid = rasterClassificationInput.IsValid(out var err2);
																									rasNodeInfoData.Properties["Status"] = (isValid ? "Valid" : ("Invalid: " + err2));
																									Classification classification = rasterClassificationInput.Classification;
																									if (classification != null && classification.Count > 0)
																									{
																										rasNodeInfoData.Properties["Classes"] = rasterClassificationInput.Classification.Count;
																									}
																									IRasterPyramid<int> resampler = rasterClassificationInput.GetResampler();
																									if (resampler != null)
																									{
																										RasterDefinition rasterDefinition = resampler.RasterDefinition;
																										rasNodeInfoData.Properties["Cell Size"] = $"{rasterDefinition.CellSize:F2}";
																										rasNodeInfoData.Properties["Dimensions"] = $"{rasterDefinition.Cols} x {rasterDefinition.Rows}";
																									}
																									if (rasterClassificationInput.Signature != null)
																									{
																										rasNodeInfoData.Properties["File Size"] = FormatFileSize(rasterClassificationInput.Signature.FileSize);
																										rasNodeInfoData.Properties["Last Modified"] = rasterClassificationInput.Signature.DateLastModifiedUTC;
																									}
																								}
																							}
																							else
																							{
																								rasNodeInfoData.Name = terrainInput.Name;
																								rasNodeInfoData.Properties["Type"] = terrainInput.TypeName;
																								terrainInput.IsValid(out var err3);
																								rasNodeInfoData.Properties["Status"] = (string.IsNullOrEmpty(err3) ? "Valid" : err3);
																							}
																						}
																						else
																						{
																							rasNodeInfoData.Name = rasterTerrainInput.Name;
																							rasNodeInfoData.Properties["Type"] = rasterTerrainInput.TypeName;
																							rasNodeInfoData.Filename = rasterTerrainInput.Filename;
																							bool isValid2 = rasterTerrainInput.IsValid(out var err4);
																							rasNodeInfoData.Properties["Status"] = (isValid2 ? "Valid" : ("Invalid: " + err4));
																							IRasterPyramid<float> resampler2 = rasterTerrainInput.GetResampler();
																							if (resampler2 != null)
																							{
																								RasterDefinition rasterDefinition2 = resampler2.RasterDefinition;
																								rasNodeInfoData.Properties["Cell Size"] = $"{rasterDefinition2.CellSize:F2}";
																								rasNodeInfoData.Properties["Dimensions"] = $"{rasterDefinition2.Cols} x {rasterDefinition2.Rows}";
																								int width = Math.Min(Math.Min(90, Console.WindowWidth / 2) - 4, 80);
																								int height = CalculatePreviewHeight(resampler2.ValidExtent, width);
																								rasNodeInfoData.Preview = TerrainPreviewRenderer.RenderPreview(rasterTerrainInput, width, height);
																							}
																							if (rasterTerrainInput.Signature != null)
																							{
																								rasNodeInfoData.Properties["File Size"] = FormatFileSize(rasterTerrainInput.Signature.FileSize);
																								rasNodeInfoData.Properties["Last Modified"] = rasterTerrainInput.Signature.DateLastModifiedUTC;
																							}
																						}
																					}
																					else
																					{
																						rasNodeInfoData.Name = "Property Tables";
																						rasNodeInfoData.Properties["Mesh Cells"] = meshPropertyTableSignature.MeshCellCount;
																						rasNodeInfoData.Properties["Mesh Faces"] = meshPropertyTableSignature.MeshFaceCount;
																						rasNodeInfoData.Properties["Mesh Hash"] = meshPropertyTableSignature.MeshHash.ToString("X8");
																						if (meshPropertyTableSignature.TerrainSignature != null)
																						{
																							rasNodeInfoData.Associations["Terrain"] = meshPropertyTableSignature.TerrainSignature.Name ?? "(unknown)";
																							rasNodeInfoData.Properties["Terrain Layers"] = meshPropertyTableSignature.TerrainSignature.LayerSignatures?.Count ?? 0;
																						}
																						if (meshPropertyTableSignature.NValueSignature != null)
																						{
																							rasNodeInfoData.Associations["N-Value Layer"] = meshPropertyTableSignature.NValueSignature.Name ?? "(unknown)";
																							rasNodeInfoData.Properties["N-Value Mode"] = meshPropertyTableSignature.NValueSignature.Mode.ToString();
																							if (meshPropertyTableSignature.NValueSignature.Mode == NValueSource.Constant)
																							{
																								rasNodeInfoData.Properties["N-Value"] = meshPropertyTableSignature.NValueSignature.ConstantValue;
																							}
																							else if (meshPropertyTableSignature.NValueSignature.Mode == NValueSource.ClassificationLayer)
																							{
																								rasNodeInfoData.Associations["Classification"] = meshPropertyTableSignature.NValueSignature.ClassificationLayerName ?? "(unknown)";
																							}
																						}
																						rasNodeInfoData.Properties["Date Last Modified"] = ((meshPropertyTableSignature.DateLastModified != DateTime.MinValue) ? meshPropertyTableSignature.DateLastModified.ToString() : "N/A");
																						rasNodeInfoData.Properties["Software Version"] = ((!string.IsNullOrEmpty(meshPropertyTableSignature.SoftwareVersion)) ? meshPropertyTableSignature.SoftwareVersion : "N/A");
																						rasNodeInfoData.Properties["Compute Duration"] = ((!double.IsNaN(meshPropertyTableSignature.ComputeDurationSeconds)) ? $"{meshPropertyTableSignature.ComputeDurationSeconds:F2} sec" : "N/A");
																					}
																				}
																				else
																				{
																					rasNodeInfoData.Name = "Met Stations";
																					rasNodeInfoData.Properties["Station Count"] = meteorologicStationLayer.Count;
																				}
																			}
																			else
																			{
																				rasNodeInfoData.Name = spatialBoundaryConditionBase.GetType().Name.Replace("Layer", "");
																				rasNodeInfoData.Properties["Enabled"] = spatialBoundaryConditionBase.IsEnabled;
																				if (spatialBoundaryConditionBase.IsEnabled)
																				{
																					rasNodeInfoData.Properties["Data Type"] = spatialBoundaryConditionBase.SpatialDataType.ToString();
																					if (spatialBoundaryConditionBase.Ratio != 1f)
																					{
																						rasNodeInfoData.Properties["Ratio"] = spatialBoundaryConditionBase.Ratio;
																					}
																				}
																			}
																		}
																		else
																		{
																			rasNodeInfoData.Name = "Wind";
																			rasNodeInfoData.Properties["Enabled"] = windLayer.IsEnabled;
																			if (windLayer.IsEnabled)
																			{
																				rasNodeInfoData.Properties["Data Type"] = windLayer.SpatialDataType.ToString();
																				if (windLayer.SpatialDataType == SpatialDataType.Constant)
																				{
																					rasNodeInfoData.Properties["Constant Vx"] = windLayer.ConstantVx;
																					rasNodeInfoData.Properties["Constant Vy"] = windLayer.ConstantVy;
																				}
																			}
																		}
																	}
																	else
																	{
																		rasNodeInfoData.Name = "Precipitation";
																		rasNodeInfoData.Properties["Enabled"] = precipitationLayer.IsEnabled;
																		if (precipitationLayer.IsEnabled)
																		{
																			rasNodeInfoData.Properties["Data Type"] = precipitationLayer.SpatialDataType.ToString();
																			if (precipitationLayer.SpatialDataType == SpatialDataType.Constant)
																			{
																				rasNodeInfoData.Properties["Constant Value"] = precipitationLayer.ConstantValue;
																			}
																			if (precipitationLayer.Ratio != 1f)
																			{
																				rasNodeInfoData.Properties["Ratio"] = precipitationLayer.Ratio;
																			}
																		}
																	}
																}
																else
																{
																	rasNodeInfoData.Name = "BC Lines";
																	rasNodeInfoData.Properties["Line Count"] = boundaryConditionLineLayer.Count;
																}
															}
															else
															{
																rasNodeInfoData.Name = "BC Lines";
																rasNodeInfoData.Properties["Line Count"] = bCLinesWrapper.Count;
															}
														}
														else
														{
															rasNodeInfoData.Name = "CulvertBarrelProperties";
															rasNodeInfoData.Properties["Property Count"] = barrelPropertiesLayer.Count;
														}
													}
													else
													{
														rasNodeInfoData.Name = "CulvertOpeningProperties";
														rasNodeInfoData.Properties["Opening Count"] = openingPropertiesLayer.Count;
													}
												}
												else
												{
													rasNodeInfoData.Name = "CulvertBarrels";
													rasNodeInfoData.Properties["Barrel Count"] = culvertBarrelLayer.Count;
												}
											}
											else
											{
												rasNodeInfoData.Name = "FlowAreaLayer";
												rasNodeInfoData.Properties["Flow Area Count"] = flowAreaLayer.Count;
												if (flowAreaLayer.Count > 0 && flowAreaLayer[0].Mesh != null)
												{
													rasNodeInfoData.Properties["Mesh Cells"] = flowAreaLayer[0].Mesh.CellCount;
													rasNodeInfoData.Properties["Mesh Faces"] = flowAreaLayer[0].Mesh.FaceCount;
												}
											}
										}
										else
										{
											rasNodeInfoData.Name = "TopologyLayer";
											rasNodeInfoData.Properties["Has Topology"] = topologyLayer.Topology != null;
											if (topologyLayer.Topology != null)
											{
												rasNodeInfoData.Properties["Nodes"] = topologyLayer.Topology.Nodes.Count;
												rasNodeInfoData.Properties["Arcs"] = topologyLayer.Topology.Arcs.Count;
											}
										}
									}
									else
									{
										rasNodeInfoData.Name = classificationLayer.Name;
										rasNodeInfoData.Filename = classificationLayer.Filename;
										rasNodeInfoData.Properties["File Size"] = GetFormattedFileSize(classificationLayer.Filename);
										rasNodeInfoData.Properties["Total Size"] = GetFormattedClassificationTotalSize(classificationLayer);
										rasNodeInfoData.Properties["Layer Count"] = classificationLayer.LayerCount;
										UniqueClassificationMap classificationMap = classificationLayer.ClassificationMap;
										if (classificationMap != null && classificationMap.Count > 0)
										{
											rasNodeInfoData.Properties["Classes"] = classificationLayer.ClassificationMap.Count;
										}
										if (!classificationLayer.ValidExtent.IsNull())
										{
											rasNodeInfoData.ExtentInfo = $"({classificationLayer.ValidExtent.MinX:F0}, {classificationLayer.ValidExtent.MinY:F0}) - ({classificationLayer.ValidExtent.MaxX:F0}, {classificationLayer.ValidExtent.MaxY:F0})";
										}
									}
								}
								else
								{
									rasNodeInfoData.Name = nValueLayer.Name;
									rasNodeInfoData.Filename = nValueLayer.Filename;
									rasNodeInfoData.Properties["File Size"] = GetFormattedFileSize(nValueLayer.Filename);
									rasNodeInfoData.Properties["Mode"] = nValueLayer.Mode.ToString();
									if (nValueLayer.Mode == NValueSource.Constant)
									{
										rasNodeInfoData.Properties["Constant Value"] = nValueLayer.ConstantValue;
									}
									else if (nValueLayer.Mode == NValueSource.ClassificationLayer)
									{
										rasNodeInfoData.Associations["Classification Layer"] = nValueLayer.ClassificationLayerName ?? "(none)";
										NVTable interpretationTable = nValueLayer.InterpretationTable;
										if (interpretationTable != null && interpretationTable.Count > 0)
										{
											rasNodeInfoData.Properties["Interpretation Entries"] = nValueLayer.InterpretationTable.Count;
										}
										if (nValueLayer.AssociatedClassificationLayer != null)
										{
											rasNodeInfoData.Properties["Total Size"] = GetFormattedClassificationTotalSize(nValueLayer.AssociatedClassificationLayer);
										}
									}
									if (!nValueLayer.ValidExtent.IsNull())
									{
										rasNodeInfoData.ExtentInfo = $"({nValueLayer.ValidExtent.MinX:F0}, {nValueLayer.ValidExtent.MinY:F0}) - ({nValueLayer.ValidExtent.MaxX:F0}, {nValueLayer.ValidExtent.MaxY:F0})";
									}
								}
							}
							else
							{
								rasNodeInfoData.Name = terrain.Name;
								rasNodeInfoData.Filename = terrain.Filename;
								rasNodeInfoData.Properties["File Size"] = GetFormattedFileSize(terrain.Filename);
								rasNodeInfoData.Properties["Total Size"] = GetFormattedTerrainTotalSize(terrain);
								rasNodeInfoData.Properties["Layer Count"] = terrain.LayerCount;
								if (!terrain.ValidExtent.IsNull())
								{
									rasNodeInfoData.ExtentInfo = $"({terrain.ValidExtent.MinX:F0}, {terrain.ValidExtent.MinY:F0}) - ({terrain.ValidExtent.MaxX:F0}, {terrain.ValidExtent.MaxY:F0})";
								}
							}
						}
						else
						{
							rasNodeInfoData.Name = result.Name;
							rasNodeInfoData.Filename = result.Filename;
							rasNodeInfoData.Properties["File Size"] = GetFormattedFileSize(result.Filename);
							rasNodeInfoData.Properties["Profile Count"] = result.ProfileCount;
							rasNodeInfoData.Associations["Embedded Plan"] = result.Plan?.Name ?? "(none)";
							rasNodeInfoData.Associations["Embedded Geometry"] = result.Geometry?.Name ?? "(none)";
							rasNodeInfoData.Associations["Embedded BC"] = result.BoundaryConditions?.Name ?? "(none)";
							if (result.ComputeSummary != null)
							{
								ComputeSummary computeSummary2 = result.ComputeSummary;
								if (!string.IsNullOrEmpty(computeSummary2.SoftwareVersion))
								{
									rasNodeInfoData.Properties["Software Version"] = computeSummary2.SoftwareVersion;
								}
								if (!string.IsNullOrEmpty(computeSummary2.Solver))
								{
									rasNodeInfoData.Properties["Solver"] = computeSummary2.Solver;
								}
								rasNodeInfoData.Properties["Computed Successfully"] = computeSummary2.Success;
								if (!computeSummary2.Success && !string.IsNullOrEmpty(computeSummary2.FailureReason))
								{
									rasNodeInfoData.Properties["Failure Reason"] = computeSummary2.FailureReason;
								}
								if (computeSummary2.StartTime != default(DateTime))
								{
									rasNodeInfoData.Properties["Compute Start"] = computeSummary2.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
								}
								if (computeSummary2.TotalDuration != default(TimeSpan))
								{
									rasNodeInfoData.Properties["Total Duration"] = computeSummary2.TotalDuration.ToString("hh\\:mm\\:ss\\.fff");
								}
								if (computeSummary2.SolverInitDuration != default(TimeSpan))
								{
									rasNodeInfoData.Properties["Solver Init"] = computeSummary2.SolverInitDuration.ToString("hh\\:mm\\:ss\\.fff");
								}
								if (computeSummary2.OutputDuration != default(TimeSpan))
								{
									rasNodeInfoData.Properties["Output Duration"] = computeSummary2.OutputDuration.ToString("hh\\:mm\\:ss\\.fff");
								}
								if (computeSummary2.GlobalRetryCount > 0)
								{
									rasNodeInfoData.Properties["Global Retry Count"] = computeSummary2.GlobalRetryCount;
								}
							}
						}
					}
					else
					{
						rasNodeInfoData.Name = boundaryCondition.Name;
						rasNodeInfoData.Filename = boundaryCondition.Filename;
						rasNodeInfoData.Properties["File Size"] = GetFormattedFileSize(boundaryCondition.Filename);
						rasNodeInfoData.Properties["Start Time"] = boundaryCondition.StartTime;
						rasNodeInfoData.Properties["End Time"] = boundaryCondition.EndTime;
						rasNodeInfoData.Properties["Duration"] = boundaryCondition.Duration;
					}
				}
				else
				{
					rasNodeInfoData.Name = geometry.Name;
					rasNodeInfoData.Filename = geometry.Filename;
					rasNodeInfoData.Properties["File Size"] = GetFormattedFileSize(geometry.Filename);
					GeometryAssociation geometryAssociation2 = _project.GetGeometryAssociation(geometry);
					rasNodeInfoData.Associations["Terrain"] = geometryAssociation2?.Terrain?.Name ?? "(none)";
					rasNodeInfoData.Associations["N-Value Layer"] = geometryAssociation2?.NValueLayer?.Name ?? "(none)";
					if (!geometry.Extent.IsNull())
					{
						rasNodeInfoData.ExtentInfo = $"({geometry.Extent.MinX:F0}, {geometry.Extent.MinY:F0}) - ({geometry.Extent.MaxX:F0}, {geometry.Extent.MaxY:F0})";
					}
					if (geometry.Mesh != null)
					{
						rasNodeInfoData.Properties["Mesh Cells"] = geometry.Mesh.CellCount;
						rasNodeInfoData.Properties["Mesh Faces"] = geometry.Mesh.FaceCount;
					}
				}
			}
			else
			{
				rasNodeInfoData.Name = plan.Name;
				rasNodeInfoData.Filename = plan.Filename;
				rasNodeInfoData.Properties["File Size"] = GetFormattedFileSize(plan.Filename);
				PlanAssociation planAssociation2 = _project.GetPlanAssociation(plan);
				rasNodeInfoData.Associations["Geometry"] = planAssociation2?.Geometry?.Name ?? "(none)";
				rasNodeInfoData.Associations["Boundary Condition"] = planAssociation2?.BoundaryCondition?.Name ?? "(none)";
				rasNodeInfoData.Properties["Start Time"] = plan.StartTime;
				rasNodeInfoData.Properties["End Time"] = plan.EndTime;
				rasNodeInfoData.Properties["Compute Timestep"] = plan.ComputeTimestep;
				rasNodeInfoData.Properties["Solver"] = plan.SolverType;
			}
		}
		else
		{
			rasNodeInfoData.Name = project.Name;
			rasNodeInfoData.Filename = project.Filename;
			rasNodeInfoData.Properties["Units"] = project.Units;
			if (project.Projection != null)
			{
				string name = project.Projection.GetName();
				if (!string.IsNullOrEmpty(name))
				{
					rasNodeInfoData.Properties["Projection"] = name;
				}
				if (project.Projection.TryGetEPSGCode(out var epsg))
				{
					rasNodeInfoData.Properties["EPSG"] = epsg;
				}
				string sanitizedLinearUnitName = project.Projection.GetSanitizedLinearUnitName();
				if (!string.IsNullOrEmpty(sanitizedLinearUnitName))
				{
					rasNodeInfoData.Properties["Linear Units"] = sanitizedLinearUnitName;
				}
			}
			else if (project.ProjectionDefinition != null)
			{
				rasNodeInfoData.Properties["Projection Type"] = project.ProjectionDefinition.Type.ToString();
				if (!string.IsNullOrEmpty(project.ProjectionDefinition.Value))
				{
					rasNodeInfoData.Properties["Projection Value"] = project.ProjectionDefinition.Value;
				}
			}
			rasNodeInfoData.Properties["Plans"] = project.Plans.Count;
			rasNodeInfoData.Properties["Geometries"] = project.Geometries.Count;
			rasNodeInfoData.Properties["Results"] = project.Results.Count;
			if (!string.IsNullOrEmpty(project.Description))
			{
				rasNodeInfoData.Properties["Description"] = project.Description;
			}
		}
		if (selectedNode.DataSource is CoreLayerBase coreLayerBase)
		{
			if (!string.IsNullOrEmpty(coreLayerBase.DateLastModified))
			{
				rasNodeInfoData.Properties["Last Modified"] = coreLayerBase.DateLastModified;
			}
			if (!string.IsNullOrEmpty(coreLayerBase.DateCreated))
			{
				rasNodeInfoData.Properties["Created"] = coreLayerBase.DateCreated;
			}
			if (!string.IsNullOrEmpty(coreLayerBase.LoadedFileVersion))
			{
				rasNodeInfoData.Properties["File Version"] = coreLayerBase.LoadedFileVersion;
			}
			if (!string.IsNullOrEmpty(coreLayerBase.LoadedSoftwareVersion))
			{
				rasNodeInfoData.Properties["Software Version"] = coreLayerBase.LoadedSoftwareVersion;
			}
			if (!string.IsNullOrEmpty(coreLayerBase.OriginSoftware))
			{
				rasNodeInfoData.Properties["Origin Software"] = coreLayerBase.OriginSoftware;
			}
			if (!string.IsNullOrEmpty(coreLayerBase.OriginVersion))
			{
				rasNodeInfoData.Properties["Origin Version"] = coreLayerBase.OriginVersion;
			}
		}
		_state.SelectedNodeInfo = rasNodeInfoData;
	}

	private bool ProcessInput(ConsoleKeyInfo key)
	{
		return _state.Mode switch
		{
			ExplorerMode.Tree => ProcessTreeModeInput(key), 
			ExplorerMode.FeatureTable => ProcessFeatureTableInput(key), 
			ExplorerMode.PlanEditor => ProcessPlanEditorInput(key), 
			ExplorerMode.GeometryEditor => ProcessGeometryEditorInput(key), 
			_ => ProcessTreeModeInput(key), 
		};
	}

	private bool ProcessTreeModeInput(ConsoleKeyInfo key)
	{
		if (_state.FocusOnInfoPanel)
		{
			return ProcessInfoPanelInput(key);
		}
		return ProcessTreeInput(key);
	}

	private bool ProcessTreeInput(ConsoleKeyInfo key)
	{
		int windowHeightOffset = Console.WindowHeight - 4;
		switch (key.Key)
		{
		case ConsoleKey.UpArrow:
			_state.MoveCursor(-1, windowHeightOffset);
			break;
		case ConsoleKey.DownArrow:
			_state.MoveCursor(1, windowHeightOffset);
			break;
		case ConsoleKey.PageUp:
			_state.MoveCursor(-windowHeightOffset, windowHeightOffset);
			break;
		case ConsoleKey.PageDown:
			_state.MoveCursor(windowHeightOffset, windowHeightOffset);
			break;
		case ConsoleKey.Home:
			_state.CursorIndex = 0;
			_state.ScrollOffset = 0;
			break;
		case ConsoleKey.End:
			_state.CursorIndex = _state.VisibleNodes.Count - 1;
			_state.MoveCursor(0, windowHeightOffset);
			break;
		case ConsoleKey.RightArrow:
			if (!TryExpandOrOpen() && _state.ShowInfoPanel)
			{
				_state.FocusOnInfoPanel = true;
				_state.SelectedPropertyIndex = 0;
			}
			break;
		case ConsoleKey.Enter:
			TryExpandOrOpen();
			break;
		case ConsoleKey.LeftArrow:
			CollapseOrGoToParent();
			break;
		case ConsoleKey.A:
			CycleInfoPanelMode();
			break;
		case ConsoleKey.C:
			CopySelectedPath();
			break;
		case ConsoleKey.X:
			ShowActionMenu();
			break;
		case ConsoleKey.E:
			TryOpenEditor();
			break;
		case ConsoleKey.Escape:
		case ConsoleKey.Q:
			return false;
		}
		UpdateSelectedNodeInfo();
		return true;
	}

	private void ShowActionMenu()
	{
		RasTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode != null && selectedNode.DataSource != null)
		{
			new ActionScreen(_actionRegistry, selectedNode, _project).Show();
			Console.Clear();
			Console.SetCursorPosition(0, 0);
		}
	}

	private bool ProcessInfoPanelInput(ConsoleKeyInfo key)
	{
		int countProperties = CountProperties();
		int windowHeightOffset = Console.WindowHeight - 10;
		switch (key.Key)
		{
		case ConsoleKey.UpArrow:
			_state.MoveInfoPanelCursor(-1, windowHeightOffset, countProperties);
			break;
		case ConsoleKey.DownArrow:
			_state.MoveInfoPanelCursor(1, windowHeightOffset, countProperties);
			break;
		case ConsoleKey.PageUp:
			_state.MoveInfoPanelCursor(-windowHeightOffset, windowHeightOffset, countProperties);
			break;
		case ConsoleKey.PageDown:
			_state.MoveInfoPanelCursor(windowHeightOffset, windowHeightOffset, countProperties);
			break;
		case ConsoleKey.Home:
			_state.SelectedPropertyIndex = 0;
			_state.InfoPanelScrollOffset = 0;
			break;
		case ConsoleKey.End:
			_state.SelectedPropertyIndex = Math.Max(0, countProperties - 1);
			_state.AdjustInfoPanelScroll(windowHeightOffset);
			break;
		case ConsoleKey.Escape:
		case ConsoleKey.LeftArrow:
			_state.FocusOnInfoPanel = false;
			break;
		case ConsoleKey.A:
			CycleInfoPanelMode();
			_state.FocusOnInfoPanel = false;
			break;
		case ConsoleKey.C:
			CopySelectedProperty();
			break;
		case ConsoleKey.X:
			ShowActionMenu();
			break;
		case ConsoleKey.Q:
			return false;
		}
		return true;
	}

	private int CountProperties()
	{
		RasNodeInfoData selectedNodeInfo = _state.SelectedNodeInfo;
		if (selectedNodeInfo == null)
		{
			return 0;
		}
		int ordinal = 0;
		if (!string.IsNullOrEmpty(selectedNodeInfo.Name))
		{
			ordinal++;
		}
		if (!string.IsNullOrEmpty(selectedNodeInfo.Filename))
		{
			ordinal++;
		}
		ordinal += selectedNodeInfo.Associations?.Count ?? 0;
		ordinal += selectedNodeInfo.Properties?.Count ?? 0;
		if (!string.IsNullOrEmpty(selectedNodeInfo.ExtentInfo))
		{
			ordinal++;
		}
		return ordinal;
	}

	private bool TryExpandOrOpen()
	{
		RasTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null)
		{
			return false;
		}
		if (CanShowFeatureTable(selectedNode))
		{
			OpenFeatureTableView(selectedNode);
			return true;
		}
		if ((selectedNode.Type == RasNodeType.Root || selectedNode.Type == RasNodeType.Collection || selectedNode.Type == RasNodeType.Geometry || selectedNode.Type == RasNodeType.BoundaryCondition || selectedNode.Type == RasNodeType.Result || selectedNode.Type == RasNodeType.Terrain || selectedNode.Type == RasNodeType.NValue || selectedNode.Type == RasNodeType.Classification || selectedNode.Children.Count > 0) && !selectedNode.IsExpanded)
		{
			if (!selectedNode.ChildrenLoaded && selectedNode.Type == RasNodeType.Collection)
			{
				LoadCollectionItems(selectedNode);
			}
			selectedNode.IsExpanded = true;
			_state.RefreshVisibleNodes();
			return true;
		}
		return false;
	}

	private bool CanShowFeatureTable(RasTreeNode node)
	{
		if (node == null || node.DataSource == null)
		{
			return false;
		}
		if (!(node.DataSource is CulvertBarrelLayer) && !(node.DataSource is BarrelPropertiesLayer) && !(node.DataSource is OpeningPropertiesLayer) && !(node.DataSource is TopologyArcsWrapper) && !(node.DataSource is TopologyRegionsWrapper))
		{
			return node.DataSource is BCLinesWrapper;
		}
		return true;
	}

	private Geometry GetParentGeometry(RasTreeNode node)
	{
		if (node.DataSource is IGeometryLayer geometryLayer)
		{
			return geometryLayer.Geometry;
		}
		for (RasTreeNode parent = node.Parent; parent != null; parent = parent.Parent)
		{
			if (parent.DataSource is Geometry result)
			{
				return result;
			}
		}
		return null;
	}

	private void OpenFeatureTableView(RasTreeNode node)
	{
		_featureTableLayer = node.DataSource;
		_featureTableParentGeometry = GetParentGeometry(node);
		FeatureTableState featureTableState = new FeatureTableState();
		Type featureRowType = FeatureTableColumnExtractor.GetFeatureRowType(_featureTableLayer);
		if (featureRowType != null)
		{
			featureTableState.Columns = FeatureTableColumnExtractor.GetColumns(featureRowType);
		}
		else
		{
			featureTableState.Columns = new List<ColumnInfo>();
		}
		featureTableState.Features = FeatureTableColumnExtractor.GetFeatures(_featureTableLayer);
		featureTableState.LayerName = FeatureTableColumnExtractor.GetLayerName(_featureTableLayer);
		_state.FeatureTableState = featureTableState;
		_state.Mode = ExplorerMode.FeatureTable;
	}

	private bool ProcessFeatureTableInput(ConsoleKeyInfo key)
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		if (featureTableState == null)
		{
			_state.Mode = ExplorerMode.Tree;
			return true;
		}
		if (featureTableState.ShowingSavePrompt)
		{
			return ProcessFeatureTableSavePrompt(key);
		}
		if (featureTableState.ShowingDeleteConfirm)
		{
			return ProcessFeatureTableDeleteConfirm(key);
		}
		if (featureTableState.IsEditingCell)
		{
			return ProcessFeatureTableEditInput(key);
		}
		if (featureTableState.IsInteractiveMode)
		{
			return ProcessFeatureTableInteractiveInput(key);
		}
		return ProcessFeatureTableScrollInput(key);
	}

	private bool ProcessFeatureTableScrollInput(ConsoleKeyInfo key)
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		int windowHeightOffset = Console.WindowHeight - 9;
		int visibleCols = CalculateFeatureTableVisibleCols();
		switch (key.Key)
		{
		case ConsoleKey.Escape:
			_state.Mode = ExplorerMode.Tree;
			_state.FeatureTableState = null;
			_featureTableLayer = null;
			_featureTableParentGeometry = null;
			break;
		case ConsoleKey.UpArrow:
			featureTableState.MoveScroll(-1, 0, windowHeightOffset, visibleCols);
			break;
		case ConsoleKey.DownArrow:
			featureTableState.MoveScroll(1, 0, windowHeightOffset, visibleCols);
			break;
		case ConsoleKey.LeftArrow:
			featureTableState.MoveScroll(0, -1, windowHeightOffset, visibleCols);
			break;
		case ConsoleKey.RightArrow:
			featureTableState.MoveScroll(0, 1, windowHeightOffset, visibleCols);
			break;
		case ConsoleKey.PageUp:
			featureTableState.MoveScroll(-windowHeightOffset, 0, windowHeightOffset, visibleCols);
			break;
		case ConsoleKey.PageDown:
			featureTableState.MoveScroll(windowHeightOffset, 0, windowHeightOffset, visibleCols);
			break;
		case ConsoleKey.Home:
			featureTableState.RowScrollOffset = 0;
			featureTableState.ColScrollOffset = 0;
			break;
		case ConsoleKey.End:
			featureTableState.RowScrollOffset = Math.Max(0, featureTableState.FeatureCount - 1);
			break;
		case ConsoleKey.I:
			featureTableState.EnterInteractiveMode();
			break;
		case ConsoleKey.C:
			CopyFeatureTableAsCsv();
			break;
		}
		return true;
	}

	private bool ProcessFeatureTableInteractiveInput(ConsoleKeyInfo key)
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		int visibleRows = Console.WindowHeight - 9;
		int visibleCols = CalculateFeatureTableVisibleCols();
		switch (key.Key)
		{
		case ConsoleKey.Escape:
			if (featureTableState.HasUnsavedChanges)
			{
				featureTableState.ShowingSavePrompt = true;
			}
			else
			{
				featureTableState.ResetInteractiveState();
			}
			break;
		case ConsoleKey.UpArrow:
			featureTableState.MoveCellCursor(-1, 0, visibleRows, visibleCols);
			break;
		case ConsoleKey.DownArrow:
			featureTableState.MoveCellCursor(1, 0, visibleRows, visibleCols);
			break;
		case ConsoleKey.LeftArrow:
			featureTableState.MoveCellCursor(0, -1, visibleRows, visibleCols);
			break;
		case ConsoleKey.RightArrow:
			featureTableState.MoveCellCursor(0, 1, visibleRows, visibleCols);
			break;
		case ConsoleKey.Enter:
			if (featureTableState.CellCursorCol >= 0)
			{
				StartFeatureTableCellEdit();
			}
			break;
		case ConsoleKey.D:
			if (featureTableState.CellCursorCol == -1)
			{
				featureTableState.ToggleRowDeletion(featureTableState.CellCursorRow);
			}
			break;
		case ConsoleKey.C:
			CopyFeatureTableSelectedCell();
			break;
		case ConsoleKey.W:
			if ((key.Modifiers & ConsoleModifiers.Control) != 0)
			{
				SaveFeatureTableChanges();
			}
			break;
		}
		return true;
	}

	private bool ProcessFeatureTableEditInput(ConsoleKeyInfo key)
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		switch (key.Key)
		{
		case ConsoleKey.Enter:
			CommitFeatureTableCellEdit();
			break;
		case ConsoleKey.Escape:
			CancelFeatureTableCellEdit();
			break;
		case ConsoleKey.Backspace:
			if (featureTableState.EditBuffer.Length > 0)
			{
				featureTableState.EditBuffer = featureTableState.EditBuffer.Substring(0, featureTableState.EditBuffer.Length - 1);
			}
			featureTableState.EditError = null;
			break;
		default:
			if (!char.IsControl(key.KeyChar))
			{
				featureTableState.EditBuffer += key.KeyChar;
				featureTableState.EditError = null;
			}
			break;
		}
		return true;
	}

	private bool ProcessFeatureTableSavePrompt(ConsoleKeyInfo key)
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		switch (char.ToUpper(key.KeyChar))
		{
		case 'S':
			SaveFeatureTableChanges();
			featureTableState.ShowingSavePrompt = false;
			featureTableState.ResetInteractiveState();
			break;
		case 'D':
			DiscardFeatureTableChanges();
			featureTableState.ShowingSavePrompt = false;
			featureTableState.ResetInteractiveState();
			break;
		case 'C':
			featureTableState.ShowingSavePrompt = false;
			break;
		}
		return true;
	}

	private bool ProcessFeatureTableDeleteConfirm(ConsoleKeyInfo key)
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		switch (char.ToUpper(key.KeyChar))
		{
		case 'Y':
			featureTableState.ToggleRowDeletion(featureTableState.CellCursorRow);
			featureTableState.ShowingDeleteConfirm = false;
			break;
		case '\u001b':
		case 'N':
			featureTableState.ShowingDeleteConfirm = false;
			break;
		}
		return true;
	}

	private void StartFeatureTableCellEdit()
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		int cellCursorCol = featureTableState.CellCursorCol;
		if (cellCursorCol >= 0 && cellCursorCol < featureTableState.ColumnCount)
		{
			ColumnInfo columnInfo = featureTableState.Columns[cellCursorCol];
			if (columnInfo.IsReadOnly)
			{
				featureTableState.EditError = "Column '" + columnInfo.Name + "' is read-only";
				return;
			}
			object cellValue = featureTableState.GetCellValue(featureTableState.CellCursorRow, cellCursorCol);
			featureTableState.EditBuffer = FeatureTableDataAccessor.FormatValue(cellValue);
			featureTableState.IsEditingCell = true;
			featureTableState.EditError = null;
		}
	}

	private void CommitFeatureTableCellEdit()
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		int cellCursorCol = featureTableState.CellCursorCol;
		if (cellCursorCol < 0 || cellCursorCol >= featureTableState.ColumnCount)
		{
			CancelFeatureTableCellEdit();
			return;
		}
		ColumnInfo columnInfo = featureTableState.Columns[cellCursorCol];
		if (!FeatureTableDataAccessor.TryParseValue(featureTableState.EditBuffer, columnInfo.ValueType, out var result))
		{
			featureTableState.EditError = "Cannot parse '" + featureTableState.EditBuffer + "' as " + columnInfo.ValueType.Name;
			return;
		}
		featureTableState.SetCellValue(featureTableState.CellCursorRow, cellCursorCol, result);
		featureTableState.IsEditingCell = false;
		featureTableState.EditBuffer = "";
		featureTableState.EditError = null;
	}

	private void CancelFeatureTableCellEdit()
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		featureTableState.IsEditingCell = false;
		featureTableState.EditBuffer = "";
		featureTableState.EditError = null;
	}

	private void SaveFeatureTableChanges()
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		if (!featureTableState.HasUnsavedChanges)
		{
			return;
		}
		try
		{
			foreach (KeyValuePair<(int, int), object> pendingChange in featureTableState.PendingChanges)
			{
				(int, int) key = pendingChange.Key;
				int ordinal = key.Item1;
				int ordinal2 = key.Item2;
				object pendingChangeTerm = pendingChange.Value;
				if (ordinal >= 0 && ordinal < featureTableState.FeatureCount && ordinal2 >= 0 && ordinal2 < featureTableState.ColumnCount)
				{
					object feature = featureTableState.Features[ordinal];
					ColumnInfo column = featureTableState.Columns[ordinal2];
					FeatureTableDataAccessor.SetValue(feature, column, pendingChangeTerm);
				}
			}
			if (featureTableState.DeletedRows.Count > 0 && _featureTableLayer is IList featureTableLayer)
			{
				foreach (int orderByDescending in featureTableState.DeletedRows.OrderByDescending((int r) => r))
				{
					if (orderByDescending >= 0 && orderByDescending < featureTableLayer.Count)
					{
						featureTableLayer.RemoveAt(orderByDescending);
					}
				}
			}
			if (_featureTableParentGeometry != null && !string.IsNullOrEmpty(_featureTableParentGeometry.Filename))
			{
				_featureTableParentGeometry.Save();
			}
			featureTableState.ClearPendingChanges();
			featureTableState.StatusMessage = "Changes saved successfully";
			if (featureTableState.DeletedRows.Count > 0)
			{
				featureTableState.Features = FeatureTableColumnExtractor.GetFeatures(_featureTableLayer);
			}
		}
		catch (Exception ex)
		{
			featureTableState.EditError = "Save failed: " + ex.Message;
		}
	}

	private void DiscardFeatureTableChanges()
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		featureTableState.ClearPendingChanges();
		featureTableState.StatusMessage = "Changes discarded";
	}

	private void CopyFeatureTableSelectedCell()
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		int cellCursorRow = featureTableState.CellCursorRow;
		int cellCursorCol = featureTableState.CellCursorCol;
		if (cellCursorCol == -1)
		{
			CopyFeatureTableRowAsCsv(cellCursorRow);
			return;
		}
		CopyToClipboard(FeatureTableDataAccessor.FormatValue(featureTableState.GetCellValue(cellCursorRow, cellCursorCol)));
		featureTableState.StatusMessage = "Cell copied to clipboard";
	}

	private void CopyFeatureTableRowAsCsv(int row)
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		if (row >= 0 && row < featureTableState.FeatureCount)
		{
			List<string> stringList = new List<string>();
			for (int i = 0; i < featureTableState.ColumnCount; i++)
			{
				object cellValue = featureTableState.GetCellValue(row, i);
				stringList.Add(FormatCsvValue(FeatureTableDataAccessor.FormatValue(cellValue)));
			}
			CopyToClipboard(string.Join(",", stringList));
			featureTableState.StatusMessage = "Row copied to clipboard";
		}
	}

	private void CopyFeatureTableAsCsv()
	{
		FeatureTableState featureTableState = _state.FeatureTableState;
		long featureCountFeatureTableStateTerm = (long)featureTableState.FeatureCount * (long)featureTableState.ColumnCount * 15;
		if (featureCountFeatureTableStateTerm > 102400)
		{
			featureTableState.EditError = $"Table too large to copy (~{featureCountFeatureTableStateTerm / 1024}KB, limit 100KB)";
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		List<string> stringList = new List<string>();
		foreach (ColumnInfo column in featureTableState.Columns)
		{
			stringList.Add(FormatCsvValue(column.Name));
		}
		stringBuilder.AppendLine(string.Join(",", stringList));
		for (int i = 0; i < featureTableState.FeatureCount; i++)
		{
			List<string> stringListList = new List<string>();
			for (int j = 0; j < featureTableState.ColumnCount; j++)
			{
				object cellValue = featureTableState.GetCellValue(i, j);
				stringListList.Add(FormatCsvValue(FeatureTableDataAccessor.FormatValue(cellValue)));
			}
			stringBuilder.AppendLine(string.Join(",", stringListList));
		}
		string stringBuilder2 = stringBuilder.ToString();
		if (stringBuilder2.Length > 102400)
		{
			featureTableState.EditError = $"Table too large to copy ({stringBuilder2.Length / 1024}KB, limit 100KB)";
		}
		else
		{
			CopyToClipboard(stringBuilder2);
			featureTableState.StatusMessage = "Table copied to clipboard";
		}
	}

	private int CalculateFeatureTableVisibleCols()
	{
		int ordinal = 14;
		int ordinal2 = 8;
		int windowWidthConsoleTerm = Console.WindowWidth - ordinal2 - 4;
		return Math.Max(1, windowWidthConsoleTerm / (ordinal + 3));
	}

	private static string FormatCsvValue(string textValue)
	{
		if (string.IsNullOrEmpty(textValue))
		{
			return "";
		}
		if (textValue.Contains(',') || textValue.Contains('"') || textValue.Contains('\n'))
		{
			return "\"" + textValue.Replace("\"", "\"\"") + "\"";
		}
		return textValue;
	}

	private void CollapseOrGoToParent()
	{
		RasTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode == null)
		{
			return;
		}
		if (selectedNode.IsExpanded && selectedNode.Children.Count > 0)
		{
			selectedNode.IsExpanded = false;
			_state.RefreshVisibleNodes();
		}
		else if (selectedNode.Parent != null)
		{
			int cursorIndex = _state.VisibleNodes.IndexOf(selectedNode.Parent);
			if (cursorIndex >= 0)
			{
				_state.CursorIndex = cursorIndex;
				_state.MoveCursor(0, Console.WindowHeight - 4);
			}
		}
	}

	private void CycleInfoPanelMode()
	{
		RasExplorerState state = _state;
		state.InfoPanelMode = _state.InfoPanelMode switch
		{
			InfoPanelMode.PropertiesAndPreview => InfoPanelMode.PropertiesOnly, 
			InfoPanelMode.PropertiesOnly => InfoPanelMode.None, 
			InfoPanelMode.None => InfoPanelMode.PropertiesAndPreview, 
			_ => InfoPanelMode.PropertiesAndPreview, 
		};
	}

	private void CopySelectedPath()
	{
		RasTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode != null)
		{
			string fullPath = selectedNode.FullPath;
			if (selectedNode.DataSource is Plan plan && !string.IsNullOrEmpty(plan.Filename))
			{
				fullPath = plan.Filename;
			}
			else if (selectedNode.DataSource is Geometry geometry && !string.IsNullOrEmpty(geometry.Filename))
			{
				fullPath = geometry.Filename;
			}
			else if (selectedNode.DataSource is BoundaryCondition boundaryCondition && !string.IsNullOrEmpty(boundaryCondition.Filename))
			{
				fullPath = boundaryCondition.Filename;
			}
			else if (selectedNode.DataSource is Result result && !string.IsNullOrEmpty(result.Filename))
			{
				fullPath = result.Filename;
			}
			else if (selectedNode.DataSource is Terrain terrain && !string.IsNullOrEmpty(terrain.Filename))
			{
				fullPath = terrain.Filename;
			}
			CopyToClipboard(fullPath);
		}
	}

	private void CopySelectedProperty()
	{
		RasNodeInfoData selectedNodeInfo = _state.SelectedNodeInfo;
		if (selectedNodeInfo == null)
		{
			return;
		}
		List<string> stringList = new List<string>();
		if (!string.IsNullOrEmpty(selectedNodeInfo.Name))
		{
			stringList.Add("Name: " + selectedNodeInfo.Name);
		}
		if (!string.IsNullOrEmpty(selectedNodeInfo.Filename))
		{
			stringList.Add("File: " + Path.GetFileName(selectedNodeInfo.Filename));
		}
		if (selectedNodeInfo.Associations != null)
		{
			foreach (KeyValuePair<string, string> association in selectedNodeInfo.Associations)
			{
				stringList.Add(association.Key + ": " + association.Value);
			}
		}
		if (selectedNodeInfo.Properties != null)
		{
			foreach (KeyValuePair<string, object> property in selectedNodeInfo.Properties)
			{
				stringList.Add($"{property.Key}: {property.Value}");
			}
		}
		if (!string.IsNullOrEmpty(selectedNodeInfo.ExtentInfo))
		{
			stringList.Add("Extent: " + selectedNodeInfo.ExtentInfo);
		}
		if (_state.SelectedPropertyIndex >= 0 && _state.SelectedPropertyIndex < stringList.Count)
		{
			CopyToClipboard(stringList[_state.SelectedPropertyIndex]);
		}
	}

	private static void CopyToClipboard(string textValue)
	{
		if (!DesktopClipboard.TryCopy(textValue)) Console.Error.WriteLine("Clipboard is unavailable.");
	}

	private static int CalculatePreviewHeight(Extent extent, int width)
	{
		if (extent.Width <= 0.0)
		{
			return width;
		}
		double heightOverWidth = extent.Height / extent.Width;
		int widthTerm = (int)((double)width * heightOverWidth * 2.0);
		return Math.Max(20, Math.Min(widthTerm, 60));
	}

	private static string FormatFileSize(long bytes)
	{
		string[] coefficients = new string[5] { "B", "KB", "MB", "GB", "TB" };
		int ordinal = 0;
		double bytes2 = bytes;
		while (bytes2 >= 1024.0 && ordinal < coefficients.Length - 1)
		{
			bytes2 /= 1024.0;
			ordinal++;
		}
		return $"{bytes2:F1} {coefficients[ordinal]}";
	}

	private static string GetFormattedFileSize(string filename)
	{
		try
		{
			if (!string.IsNullOrEmpty(filename) && File.Exists(filename))
			{
				return FormatFileSize(new FileInfo(filename).Length);
			}
		}
		catch
		{
		}
		return "N/A";
	}

	private static string GetFormattedTerrainTotalSize(Terrain terrain)
	{
		try
		{
			long ordinal = 0L;
			if (!string.IsNullOrEmpty(terrain.Filename) && File.Exists(terrain.Filename))
			{
				ordinal += new FileInfo(terrain.Filename).Length;
			}
			foreach (ITerrainInput layer in terrain.Layers)
			{
				if (layer is RasterTerrainInput { Signature: not null } rasterTerrainInput)
				{
					ordinal += rasterTerrainInput.Signature.FileSize;
				}
			}
			return (ordinal > 0) ? FormatFileSize(ordinal) : "N/A";
		}
		catch
		{
			return "N/A";
		}
	}

	private static string GetFormattedClassificationTotalSize(ClassificationLayer cl)
	{
		try
		{
			long ordinal = 0L;
			if (!string.IsNullOrEmpty(cl.Filename) && File.Exists(cl.Filename))
			{
				ordinal += new FileInfo(cl.Filename).Length;
			}
			foreach (IClassificationInput layer in cl.Layers)
			{
				if (layer is RasterClassificationInput { Signature: not null } rasterClassificationInput)
				{
					ordinal += rasterClassificationInput.Signature.FileSize;
				}
			}
			return (ordinal > 0) ? FormatFileSize(ordinal) : "N/A";
		}
		catch
		{
			return "N/A";
		}
	}

	private void TryOpenEditor()
	{
		RasTreeNode selectedNode = _state.SelectedNode;
		if (selectedNode?.DataSource is Plan)
		{
			TryOpenPlanEditor();
		}
		else if (selectedNode?.DataSource is Geometry)
		{
			TryOpenGeometryEditor();
		}
	}

	private void TryOpenPlanEditor()
	{
		if (!(_state.SelectedNode?.DataSource is Plan plan))
		{
			_state.ErrorMessage = "Select a Plan to edit";
			return;
		}
		if (plan.IsResult)
		{
			_state.ErrorMessage = "Cannot edit plans embedded in result files";
			return;
		}
		PlanEditorState planEditorState = new PlanEditorState
		{
			Plan = plan,
			Project = _project,
			Properties = PlanPropertyExtractor.GetEditableProperties(plan)
		};
		_state.PlanEditorState = planEditorState;
		_state.Mode = ExplorerMode.PlanEditor;
		_state.ErrorMessage = null;
	}

	private bool ProcessPlanEditorInput(ConsoleKeyInfo key)
	{
		PlanEditorState planEditorState = _state.PlanEditorState;
		if (planEditorState == null)
		{
			_state.Mode = ExplorerMode.Tree;
			return true;
		}
		if (planEditorState.ShowingSavePrompt)
		{
			return ProcessPlanEditorSavePrompt(key);
		}
		if (planEditorState.ShowingSaveAsPrompt)
		{
			return ProcessPlanEditorSaveAsPrompt(key);
		}
		if (planEditorState.IsEditingCell)
		{
			return ProcessPlanEditorEditInput(key);
		}
		return ProcessPlanEditorNavigationInput(key);
	}

	private bool ProcessPlanEditorNavigationInput(ConsoleKeyInfo key)
	{
		PlanEditorState planEditorState = _state.PlanEditorState;
		int windowHeightOffset = Console.WindowHeight - 10;
		PlanPropertyInfo selectedProperty = planEditorState.SelectedProperty;
		switch (key.Key)
		{
		case ConsoleKey.UpArrow:
			planEditorState.MoveCursor(-1, windowHeightOffset);
			planEditorState.ClearMessages();
			break;
		case ConsoleKey.DownArrow:
			planEditorState.MoveCursor(1, windowHeightOffset);
			planEditorState.ClearMessages();
			break;
		case ConsoleKey.PageUp:
			planEditorState.MoveCursor(-windowHeightOffset, windowHeightOffset);
			planEditorState.ClearMessages();
			break;
		case ConsoleKey.PageDown:
			planEditorState.MoveCursor(windowHeightOffset, windowHeightOffset);
			planEditorState.ClearMessages();
			break;
		case ConsoleKey.Home:
			planEditorState.CursorRow = 0;
			planEditorState.ScrollOffset = 0;
			planEditorState.ClearMessages();
			break;
		case ConsoleKey.End:
			planEditorState.CursorRow = planEditorState.Properties.Count - 1;
			planEditorState.MoveCursor(0, windowHeightOffset);
			planEditorState.ClearMessages();
			break;
		case ConsoleKey.Enter:
			if (planEditorState.IsOnAssociationRow)
			{
				planEditorState.NeedsSelectionPrompt = true;
			}
			else if (selectedProperty != null && !selectedProperty.IsReadOnly)
			{
				if (selectedProperty.IsBool || selectedProperty.IsEnum)
				{
					planEditorState.NeedsSelectionPrompt = true;
				}
				else
				{
					planEditorState.StartEditing();
				}
			}
			break;
		case ConsoleKey.Spacebar:
			if (selectedProperty != null && selectedProperty.IsBool)
			{
				planEditorState.NeedsSelectionPrompt = true;
			}
			break;
		case ConsoleKey.LeftArrow:
			if (selectedProperty != null && selectedProperty.IsEnum)
			{
				object nextEnumValue2 = PlanValueEditor.GetNextEnumValue(planEditorState.GetCurrentValue(selectedProperty), selectedProperty.ValueType, -1);
				planEditorState.SetPendingChange(selectedProperty, nextEnumValue2);
			}
			break;
		case ConsoleKey.RightArrow:
			if (selectedProperty != null && selectedProperty.IsEnum)
			{
				object nextEnumValue = PlanValueEditor.GetNextEnumValue(planEditorState.GetCurrentValue(selectedProperty), selectedProperty.ValueType, 1);
				planEditorState.SetPendingChange(selectedProperty, nextEnumValue);
			}
			break;
		case ConsoleKey.S:
			if (planEditorState.HasUnsavedChanges)
			{
				SavePlanChanges();
			}
			break;
		case ConsoleKey.A:
			if (planEditorState.HasUnsavedChanges)
			{
				planEditorState.ShowingSaveAsPrompt = true;
				planEditorState.SaveAsName = planEditorState.Plan.Name + "_Copy";
			}
			break;
		case ConsoleKey.Escape:
			if (planEditorState.HasUnsavedChanges)
			{
				planEditorState.ShowingSavePrompt = true;
			}
			else
			{
				ExitPlanEditor();
			}
			break;
		}
		return true;
	}

	private bool ProcessPlanEditorEditInput(ConsoleKeyInfo key)
	{
		PlanEditorState planEditorState = _state.PlanEditorState;
		PlanPropertyInfo selectedProperty = planEditorState.SelectedProperty;
		switch (key.Key)
		{
		case ConsoleKey.Enter:
		{
			if (PlanValueEditor.TryParseValue(planEditorState.EditBuffer, selectedProperty.ValueType, out var result, out var error))
			{
				string editError = PlanValueEditor.Validate(result, selectedProperty);
				if (editError == null)
				{
					planEditorState.SetPendingChange(selectedProperty, result);
					planEditorState.CancelEditing();
				}
				else
				{
					planEditorState.EditError = editError;
				}
			}
			else
			{
				planEditorState.EditError = error;
			}
			break;
		}
		case ConsoleKey.Escape:
			planEditorState.CancelEditing();
			break;
		case ConsoleKey.Backspace:
			if (planEditorState.EditBuffer.Length > 0)
			{
				planEditorState.EditBuffer = planEditorState.EditBuffer.Substring(0, planEditorState.EditBuffer.Length - 1);
			}
			planEditorState.EditError = null;
			break;
		default:
			if (key.KeyChar >= ' ' && key.KeyChar < '\u007f')
			{
				planEditorState.EditBuffer += key.KeyChar;
				planEditorState.EditError = null;
			}
			break;
		}
		return true;
	}

	private bool ProcessPlanEditorSavePrompt(ConsoleKeyInfo key)
	{
		PlanEditorState planEditorState = _state.PlanEditorState;
		switch (char.ToUpperInvariant(key.KeyChar))
		{
		case 'S':
			planEditorState.ShowingSavePrompt = false;
			SavePlanChanges();
			ExitPlanEditor();
			break;
		case 'A':
			planEditorState.ShowingSavePrompt = false;
			planEditorState.ShowingSaveAsPrompt = true;
			planEditorState.SaveAsName = planEditorState.Plan.Name + "_Copy";
			break;
		case 'D':
			planEditorState.ShowingSavePrompt = false;
			planEditorState.ClearPendingChanges();
			ExitPlanEditor();
			break;
		case 'C':
			planEditorState.ShowingSavePrompt = false;
			break;
		}
		return true;
	}

	private bool ProcessPlanEditorSaveAsPrompt(ConsoleKeyInfo key)
	{
		PlanEditorState planEditorState = _state.PlanEditorState;
		switch (key.Key)
		{
		case ConsoleKey.Enter:
			if (!string.IsNullOrWhiteSpace(planEditorState.SaveAsName) && SavePlanAs(planEditorState.SaveAsName))
			{
				planEditorState.ShowingSaveAsPrompt = false;
				ExitPlanEditor();
			}
			break;
		case ConsoleKey.Escape:
			planEditorState.ShowingSaveAsPrompt = false;
			planEditorState.ErrorMessage = null;
			break;
		case ConsoleKey.Backspace:
			if (planEditorState.SaveAsName.Length > 0)
			{
				planEditorState.SaveAsName = planEditorState.SaveAsName.Substring(0, planEditorState.SaveAsName.Length - 1);
			}
			planEditorState.ErrorMessage = null;
			break;
		default:
		{
			char keyChar = key.KeyChar;
			if (IsValidFilenameChar(keyChar))
			{
				planEditorState.SaveAsName += keyChar;
				planEditorState.ErrorMessage = null;
			}
			break;
		}
		}
		return true;
	}

	private void SavePlanChanges()
	{
		PlanEditorState planEditorState = _state.PlanEditorState;
		Plan plan = planEditorState.Plan;
		try
		{
			bool condition = false;
			bool condition2 = false;
			if (planEditorState.PendingChanges.Count > 0)
			{
				foreach (KeyValuePair<string, object> change in planEditorState.PendingChanges)
				{
					planEditorState.Properties.First((PlanPropertyInfo p) => p.PropertyName == change.Key).ReflectionInfo.SetValue(plan, change.Value);
				}
				plan.Save();
				condition = true;
			}
			if (planEditorState.HasUnsavedAssociationChanges)
			{
				PlanAssociation planAssociation = _project.GetPlanAssociation(plan);
				if (planAssociation != null)
				{
					if (planEditorState.HasPendingGeometryChange)
					{
						planAssociation.Geometry = planEditorState.PendingGeometry;
					}
					if (planEditorState.HasPendingBCChange)
					{
						planAssociation.BoundaryCondition = planEditorState.PendingBoundaryCondition;
					}
					_project.Save();
					condition2 = true;
				}
			}
			planEditorState.ClearPendingChanges();
			if (condition & condition2)
			{
				planEditorState.StatusMessage = "Plan and Project saved successfully";
			}
			else if (condition)
			{
				planEditorState.StatusMessage = "Plan saved successfully";
			}
			else if (condition2)
			{
				planEditorState.StatusMessage = "Project saved successfully";
			}
			else
			{
				planEditorState.StatusMessage = "No changes to save";
			}
		}
		catch (Exception ex)
		{
			planEditorState.ErrorMessage = "Save failed: " + ex.Message;
		}
	}

	private bool SavePlanAs(string newName)
	{
		PlanEditorState planEditorState = _state.PlanEditorState;
		Plan plan = planEditorState.Plan;
		try
		{
			if (string.IsNullOrWhiteSpace(newName))
			{
				planEditorState.ErrorMessage = "Name cannot be empty";
				return false;
			}
			if (_project.Plans.Any((Plan plan2) => plan2.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
			{
				planEditorState.ErrorMessage = "Plan '" + newName + "' already exists";
				return false;
			}
			foreach (KeyValuePair<string, object> change in planEditorState.PendingChanges)
			{
				planEditorState.Properties.First((PlanPropertyInfo planPropertyInfo) => planPropertyInfo.PropertyName == change.Key).ReflectionInfo.SetValue(plan, change.Value);
			}
			string filename = Plan.MakeFilename(_project.ProjectDirectory, newName);
			string directoryName = Path.GetDirectoryName(filename);
			if (!Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			plan.SaveAs(filename);
			Plan p = new Plan(filename, _project);
			_project.AddPlan(p);
			PlanAssociation planAssociation = _project.GetPlanAssociation(plan);
			if (planAssociation != null)
			{
				PlanAssociation planAssociation2 = _project.GetPlanAssociation(p);
				if (planAssociation2 != null)
				{
					planAssociation2.Geometry = planAssociation.Geometry;
					planAssociation2.BoundaryCondition = planAssociation.BoundaryCondition;
				}
			}
			_project.Save();
			planEditorState.ClearPendingChanges();
			planEditorState.StatusMessage = "Saved as '" + newName + "'";
			RefreshPlansCollection();
			return true;
		}
		catch (Exception ex)
		{
			planEditorState.ErrorMessage = "Save As failed: " + ex.Message;
			return false;
		}
	}

	private void ExitPlanEditor()
	{
		_state.PlanEditorState = null;
		_state.Mode = ExplorerMode.Tree;
	}

	private void RefreshPlansCollection()
	{
		RasTreeNode rasTreeNode = _state.Root?.Children.FirstOrDefault((RasTreeNode c) => c.Name == "Plans");
		if (rasTreeNode != null)
		{
			rasTreeNode.Children.Clear();
			rasTreeNode.ChildrenLoaded = false;
			rasTreeNode.DisplaySuffix = $"[{_project.Plans.Count}]";
			if (rasTreeNode.IsExpanded)
			{
				LoadCollectionItems(rasTreeNode);
			}
			_state.RefreshVisibleNodes();
		}
	}

	private static bool IsValidFilenameChar(char c)
	{
		if (c < ' ')
		{
			return false;
		}
		return !new char[9] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }.Contains(c);
	}

	private void ShowPlanEditorSelectionPrompt()
	{
		PlanEditorState planEditorState = _state.PlanEditorState;
		if (planEditorState == null)
		{
			return;
		}
		planEditorState.NeedsSelectionPrompt = false;
		if (planEditorState.IsOnGeometryRow)
		{
			ShowGeometrySelectionPrompt(planEditorState);
			return;
		}
		if (planEditorState.IsOnBCRow)
		{
			ShowBCSelectionPrompt(planEditorState);
			return;
		}
		PlanPropertyInfo prop = planEditorState.SelectedProperty;
		if (prop == null)
		{
			return;
		}
		object currentValue = planEditorState.GetCurrentValue(prop);
		if (prop.IsBool)
		{
			bool currentValue2 = default;
			int unity;
			if (currentValue is bool)
			{
				currentValue2 = (bool)currentValue;
				unity = 1;
			}
			else
			{
				unity = 0;
			}
			bool currentValue2Term = (byte)((uint)unity & (currentValue2 ? 1u : 0u)) != 0;
			string[] choices = new string[3] { "Yes", "No", "[dim]< Cancel >[/]" };
			string prompt = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[bold]" + prop.DisplayName + "[/]  [dim](select Cancel to abort)[/]").PageSize(5).HighlightStyle(new Style(Color.Yellow, Color.Black))
				.AddChoices(choices));
			if (!(prompt == "[dim]< Cancel >[/]"))
			{
				bool promptTerm = prompt == "Yes";
				if (promptTerm != currentValue2Term)
				{
					planEditorState.SetPendingChange(prop, promptTerm);
				}
			}
		}
		else if (prop.IsEnum)
		{
			List<EnumChoice> enumChoices = (from v in PlanValueEditor.GetEnumValues(prop.ValueType)
				select new EnumChoice
				{
					Value = v,
					DisplayName = PlanValueEditor.FormatValue(v, prop.ValueType),
					IsCancel = false
				}).ToList();
			enumChoices.Add(new EnumChoice
			{
				Value = null,
				DisplayName = "[dim]< Cancel >[/]",
				IsCancel = true
			});
			EnumChoice enumChoice = AnsiConsole.Prompt(new SelectionPrompt<EnumChoice>().Title("[bold]" + prop.DisplayName + "[/]  [dim](select Cancel to abort)[/]").PageSize(12).HighlightStyle(new Style(Color.Yellow, Color.Black))
				.UseConverter((EnumChoice c) => c.DisplayName)
				.AddChoices(enumChoices));
			if (!enumChoice.IsCancel && !object.Equals(enumChoice.Value, currentValue))
			{
				planEditorState.SetPendingChange(prop, enumChoice.Value);
			}
		}
	}

	private void ShowGeometrySelectionPrompt(PlanEditorState state)
	{
		Geometry currentGeometry = state.GetCurrentGeometry();
		List<AssociationChoice> associationChoices = new List<AssociationChoice>
		{
			new AssociationChoice
			{
				Value = null,
				DisplayName = "[dim]< None >[/]",
				IsNone = true
			}
		};
		foreach (Geometry geometry2 in _project.Geometries)
		{
			associationChoices.Add(new AssociationChoice
			{
				Value = geometry2,
				DisplayName = geometry2.Name
			});
		}
		associationChoices.Add(new AssociationChoice
		{
			DisplayName = "[dim]< Cancel >[/]",
			IsCancel = true
		});
		AssociationChoice associationChoice = AnsiConsole.Prompt(new SelectionPrompt<AssociationChoice>().Title("[bold]Geometry[/]  [dim](select Cancel to abort)[/]").PageSize(12).HighlightStyle(new Style(Color.Yellow, Color.Black))
			.UseConverter((AssociationChoice c) => c.DisplayName)
			.AddChoices(associationChoices));
		if (!associationChoice.IsCancel)
		{
			Geometry geometry = associationChoice.Value as Geometry;
			if (!object.Equals(geometry, currentGeometry))
			{
				state.SetPendingGeometry(geometry);
			}
		}
	}

	private void ShowBCSelectionPrompt(PlanEditorState state)
	{
		BoundaryCondition currentBoundaryCondition = state.GetCurrentBoundaryCondition();
		List<AssociationChoice> associationChoices = new List<AssociationChoice>
		{
			new AssociationChoice
			{
				Value = null,
				DisplayName = "[dim]< None >[/]",
				IsNone = true
			}
		};
		foreach (BoundaryCondition boundaryCondition2 in _project.BoundaryConditions)
		{
			associationChoices.Add(new AssociationChoice
			{
				Value = boundaryCondition2,
				DisplayName = boundaryCondition2.Name
			});
		}
		associationChoices.Add(new AssociationChoice
		{
			DisplayName = "[dim]< Cancel >[/]",
			IsCancel = true
		});
		AssociationChoice associationChoice = AnsiConsole.Prompt(new SelectionPrompt<AssociationChoice>().Title("[bold]Boundary Condition[/]  [dim](select Cancel to abort)[/]").PageSize(12).HighlightStyle(new Style(Color.Yellow, Color.Black))
			.UseConverter((AssociationChoice c) => c.DisplayName)
			.AddChoices(associationChoices));
		if (!associationChoice.IsCancel)
		{
			BoundaryCondition boundaryCondition = associationChoice.Value as BoundaryCondition;
			if (!object.Equals(boundaryCondition, currentBoundaryCondition))
			{
				state.SetPendingBoundaryCondition(boundaryCondition);
			}
		}
	}

	private void TryOpenGeometryEditor()
	{
		if (!(_state.SelectedNode?.DataSource is Geometry geometry))
		{
			_state.ErrorMessage = "Select a Geometry to edit";
			return;
		}
		if (geometry.IsResult)
		{
			_state.ErrorMessage = "Cannot edit geometries embedded in result files";
			return;
		}
		GeometryEditorState geometryEditorState = new GeometryEditorState
		{
			Geometry = geometry,
			Project = _project,
			Properties = GeometryPropertyExtractor.GetEditableProperties(geometry)
		};
		_state.GeometryEditorState = geometryEditorState;
		_state.Mode = ExplorerMode.GeometryEditor;
		_state.ErrorMessage = null;
	}

	private bool ProcessGeometryEditorInput(ConsoleKeyInfo key)
	{
		GeometryEditorState geometryEditorState = _state.GeometryEditorState;
		if (geometryEditorState == null)
		{
			_state.Mode = ExplorerMode.Tree;
			return true;
		}
		if (geometryEditorState.ShowingSavePrompt)
		{
			return ProcessGeometryEditorSavePrompt(key);
		}
		if (geometryEditorState.ShowingSaveAsPrompt)
		{
			return ProcessGeometryEditorSaveAsPrompt(key);
		}
		if (geometryEditorState.IsEditingCell)
		{
			return ProcessGeometryEditorEditInput(key);
		}
		return ProcessGeometryEditorNavigationInput(key);
	}

	private bool ProcessGeometryEditorNavigationInput(ConsoleKeyInfo key)
	{
		GeometryEditorState geometryEditorState = _state.GeometryEditorState;
		int windowHeightOffset = Console.WindowHeight - 10;
		GeometryPropertyInfo selectedProperty = geometryEditorState.SelectedProperty;
		switch (key.Key)
		{
		case ConsoleKey.UpArrow:
			geometryEditorState.MoveCursor(-1, windowHeightOffset);
			geometryEditorState.ClearMessages();
			break;
		case ConsoleKey.DownArrow:
			geometryEditorState.MoveCursor(1, windowHeightOffset);
			geometryEditorState.ClearMessages();
			break;
		case ConsoleKey.PageUp:
			geometryEditorState.MoveCursor(-windowHeightOffset, windowHeightOffset);
			geometryEditorState.ClearMessages();
			break;
		case ConsoleKey.PageDown:
			geometryEditorState.MoveCursor(windowHeightOffset, windowHeightOffset);
			geometryEditorState.ClearMessages();
			break;
		case ConsoleKey.Home:
			geometryEditorState.CursorRow = 0;
			geometryEditorState.ScrollOffset = 0;
			geometryEditorState.ClearMessages();
			break;
		case ConsoleKey.End:
			geometryEditorState.CursorRow = geometryEditorState.TotalRowCount - 1;
			geometryEditorState.MoveCursor(0, windowHeightOffset);
			geometryEditorState.ClearMessages();
			break;
		case ConsoleKey.Enter:
			if (geometryEditorState.IsOnAssociationRow)
			{
				geometryEditorState.NeedsSelectionPrompt = true;
			}
			else if (selectedProperty != null && !selectedProperty.IsReadOnly)
			{
				geometryEditorState.StartEditing();
			}
			break;
		case ConsoleKey.S:
			if (geometryEditorState.HasUnsavedChanges)
			{
				SaveGeometryChanges();
			}
			break;
		case ConsoleKey.A:
			if (geometryEditorState.HasUnsavedChanges)
			{
				geometryEditorState.ShowingSaveAsPrompt = true;
				geometryEditorState.SaveAsName = geometryEditorState.Geometry.Name + "_Copy";
			}
			break;
		case ConsoleKey.Escape:
			if (geometryEditorState.HasUnsavedChanges)
			{
				geometryEditorState.ShowingSavePrompt = true;
			}
			else
			{
				ExitGeometryEditor();
			}
			break;
		}
		return true;
	}

	private bool ProcessGeometryEditorEditInput(ConsoleKeyInfo key)
	{
		GeometryEditorState geometryEditorState = _state.GeometryEditorState;
		GeometryPropertyInfo selectedProperty = geometryEditorState.SelectedProperty;
		switch (key.Key)
		{
		case ConsoleKey.Enter:
			if (selectedProperty != null)
			{
				geometryEditorState.SetPendingChange(selectedProperty, geometryEditorState.EditBuffer);
				geometryEditorState.CancelEditing();
			}
			break;
		case ConsoleKey.Escape:
			geometryEditorState.CancelEditing();
			break;
		case ConsoleKey.Backspace:
			if (geometryEditorState.EditBuffer.Length > 0)
			{
				geometryEditorState.EditBuffer = geometryEditorState.EditBuffer.Substring(0, geometryEditorState.EditBuffer.Length - 1);
			}
			geometryEditorState.EditError = null;
			break;
		default:
			if (key.KeyChar >= ' ' && key.KeyChar < '\u007f')
			{
				geometryEditorState.EditBuffer += key.KeyChar;
				geometryEditorState.EditError = null;
			}
			break;
		}
		return true;
	}

	private bool ProcessGeometryEditorSavePrompt(ConsoleKeyInfo key)
	{
		GeometryEditorState geometryEditorState = _state.GeometryEditorState;
		switch (char.ToUpperInvariant(key.KeyChar))
		{
		case 'S':
			geometryEditorState.ShowingSavePrompt = false;
			SaveGeometryChanges();
			ExitGeometryEditor();
			break;
		case 'A':
			geometryEditorState.ShowingSavePrompt = false;
			geometryEditorState.ShowingSaveAsPrompt = true;
			geometryEditorState.SaveAsName = geometryEditorState.Geometry.Name + "_Copy";
			break;
		case 'D':
			geometryEditorState.ShowingSavePrompt = false;
			geometryEditorState.ClearPendingChanges();
			ExitGeometryEditor();
			break;
		case 'C':
			geometryEditorState.ShowingSavePrompt = false;
			break;
		}
		return true;
	}

	private bool ProcessGeometryEditorSaveAsPrompt(ConsoleKeyInfo key)
	{
		GeometryEditorState geometryEditorState = _state.GeometryEditorState;
		switch (key.Key)
		{
		case ConsoleKey.Enter:
			if (!string.IsNullOrWhiteSpace(geometryEditorState.SaveAsName) && SaveGeometryAs(geometryEditorState.SaveAsName))
			{
				geometryEditorState.ShowingSaveAsPrompt = false;
				ExitGeometryEditor();
			}
			break;
		case ConsoleKey.Escape:
			geometryEditorState.ShowingSaveAsPrompt = false;
			geometryEditorState.ErrorMessage = null;
			break;
		case ConsoleKey.Backspace:
			if (geometryEditorState.SaveAsName.Length > 0)
			{
				geometryEditorState.SaveAsName = geometryEditorState.SaveAsName.Substring(0, geometryEditorState.SaveAsName.Length - 1);
			}
			geometryEditorState.ErrorMessage = null;
			break;
		default:
		{
			char keyChar = key.KeyChar;
			if (IsValidFilenameChar(keyChar))
			{
				geometryEditorState.SaveAsName += keyChar;
				geometryEditorState.ErrorMessage = null;
			}
			break;
		}
		}
		return true;
	}

	private void SaveGeometryChanges()
	{
		GeometryEditorState geometryEditorState = _state.GeometryEditorState;
		Geometry geometry = geometryEditorState.Geometry;
		try
		{
			bool condition = false;
			bool condition2 = false;
			if (geometryEditorState.PendingChanges.Count > 0)
			{
				foreach (KeyValuePair<string, object> change in geometryEditorState.PendingChanges)
				{
					geometryEditorState.Properties.First((GeometryPropertyInfo p) => p.PropertyName == change.Key).ReflectionInfo.SetValue(geometry, change.Value);
				}
				geometry.Save();
				condition = true;
			}
			if (geometryEditorState.HasUnsavedAssociationChanges)
			{
				GeometryAssociation geometryAssociation = _project.GetGeometryAssociation(geometry);
				if (geometryAssociation != null)
				{
					if (geometryEditorState.HasPendingTerrainChange)
					{
						geometryAssociation.Terrain = geometryEditorState.PendingTerrain;
					}
					if (geometryEditorState.HasPendingNValueChange)
					{
						geometryAssociation.NValueLayer = geometryEditorState.PendingNValue;
					}
					_project.Save();
					condition2 = true;
				}
			}
			geometryEditorState.ClearPendingChanges();
			if (condition & condition2)
			{
				geometryEditorState.StatusMessage = "Geometry and Project saved successfully";
			}
			else if (condition)
			{
				geometryEditorState.StatusMessage = "Geometry saved successfully";
			}
			else if (condition2)
			{
				geometryEditorState.StatusMessage = "Project saved successfully";
			}
			else
			{
				geometryEditorState.StatusMessage = "No changes to save";
			}
		}
		catch (Exception ex)
		{
			geometryEditorState.ErrorMessage = "Save failed: " + ex.Message;
		}
	}

	private bool SaveGeometryAs(string newName)
	{
		GeometryEditorState geometryEditorState = _state.GeometryEditorState;
		Geometry geometry = geometryEditorState.Geometry;
		try
		{
			if (string.IsNullOrWhiteSpace(newName))
			{
				geometryEditorState.ErrorMessage = "Name cannot be empty";
				return false;
			}
			if (_project.Geometries.Any((Geometry g) => g.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
			{
				geometryEditorState.ErrorMessage = "Geometry '" + newName + "' already exists";
				return false;
			}
			foreach (KeyValuePair<string, object> change in geometryEditorState.PendingChanges)
			{
				geometryEditorState.Properties.First((GeometryPropertyInfo p) => p.PropertyName == change.Key).ReflectionInfo.SetValue(geometry, change.Value);
			}
			string filename = Geometry.MakeFilename(_project.ProjectDirectory, newName);
			string directoryName = Path.GetDirectoryName(filename);
			if (!Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			geometry.SaveAs(filename);
			Geometry geometry2 = new Geometry(filename, _project);
			_project.AddGeometry(geometry2);
			GeometryAssociation geometryAssociation = _project.GetGeometryAssociation(geometry);
			if (geometryAssociation != null)
			{
				GeometryAssociation geometryAssociation2 = _project.GetGeometryAssociation(geometry2);
				if (geometryAssociation2 != null)
				{
					geometryAssociation2.Terrain = geometryAssociation.Terrain;
					geometryAssociation2.NValueLayer = geometryAssociation.NValueLayer;
				}
			}
			_project.Save();
			geometryEditorState.ClearPendingChanges();
			geometryEditorState.StatusMessage = "Saved as '" + newName + "'";
			RefreshGeometriesCollection();
			return true;
		}
		catch (Exception ex)
		{
			geometryEditorState.ErrorMessage = "Save As failed: " + ex.Message;
			return false;
		}
	}

	private void ExitGeometryEditor()
	{
		_state.GeometryEditorState = null;
		_state.Mode = ExplorerMode.Tree;
	}

	private void RefreshGeometriesCollection()
	{
		RasTreeNode rasTreeNode = _state.Root?.Children.FirstOrDefault((RasTreeNode c) => c.Name == "Geometries");
		if (rasTreeNode != null)
		{
			rasTreeNode.Children.Clear();
			rasTreeNode.ChildrenLoaded = false;
			rasTreeNode.DisplaySuffix = $"[{_project.Geometries.Count}]";
			if (rasTreeNode.IsExpanded)
			{
				LoadCollectionItems(rasTreeNode);
			}
			_state.RefreshVisibleNodes();
		}
	}

	private void ShowGeometryEditorSelectionPrompt()
	{
		GeometryEditorState geometryEditorState = _state.GeometryEditorState;
		if (geometryEditorState != null)
		{
			geometryEditorState.NeedsSelectionPrompt = false;
			if (geometryEditorState.IsOnTerrainRow)
			{
				ShowTerrainSelectionPrompt(geometryEditorState);
			}
			else if (geometryEditorState.IsOnNValueRow)
			{
				ShowNValueSelectionPrompt(geometryEditorState);
			}
		}
	}

	private void ShowTerrainSelectionPrompt(GeometryEditorState state)
	{
		Terrain currentTerrain = state.GetCurrentTerrain();
		List<AssociationChoice> associationChoices = new List<AssociationChoice>
		{
			new AssociationChoice
			{
				Value = null,
				DisplayName = "[dim]< None >[/]",
				IsNone = true
			}
		};
		foreach (Terrain terrain2 in _project.Terrains)
		{
			associationChoices.Add(new AssociationChoice
			{
				Value = terrain2,
				DisplayName = terrain2.Name
			});
		}
		associationChoices.Add(new AssociationChoice
		{
			DisplayName = "[dim]< Cancel >[/]",
			IsCancel = true
		});
		AssociationChoice associationChoice = AnsiConsole.Prompt(new SelectionPrompt<AssociationChoice>().Title("[bold]Terrain[/]  [dim](select Cancel to abort)[/]").PageSize(12).HighlightStyle(new Style(Color.Yellow, Color.Black))
			.UseConverter((AssociationChoice c) => c.DisplayName)
			.AddChoices(associationChoices));
		if (!associationChoice.IsCancel)
		{
			Terrain terrain = associationChoice.Value as Terrain;
			if (!object.Equals(terrain, currentTerrain))
			{
				state.SetPendingTerrain(terrain);
			}
		}
	}

	private void ShowNValueSelectionPrompt(GeometryEditorState state)
	{
		NValueLayer currentNValue = state.GetCurrentNValue();
		List<AssociationChoice> associationChoices = new List<AssociationChoice>
		{
			new AssociationChoice
			{
				Value = null,
				DisplayName = "[dim]< None >[/]",
				IsNone = true
			}
		};
		foreach (NValueLayer nValue in _project.NValues)
		{
			associationChoices.Add(new AssociationChoice
			{
				Value = nValue,
				DisplayName = nValue.Name
			});
		}
		associationChoices.Add(new AssociationChoice
		{
			DisplayName = "[dim]< Cancel >[/]",
			IsCancel = true
		});
		AssociationChoice associationChoice = AnsiConsole.Prompt(new SelectionPrompt<AssociationChoice>().Title("[bold]Manning's N[/]  [dim](select Cancel to abort)[/]").PageSize(12).HighlightStyle(new Style(Color.Yellow, Color.Black))
			.UseConverter((AssociationChoice c) => c.DisplayName)
			.AddChoices(associationChoices));
		if (!associationChoice.IsCancel)
		{
			NValueLayer nValueLayer = associationChoice.Value as NValueLayer;
			if (!object.Equals(nValueLayer, currentNValue))
			{
				state.SetPendingNValue(nValueLayer);
			}
		}
	}
}
