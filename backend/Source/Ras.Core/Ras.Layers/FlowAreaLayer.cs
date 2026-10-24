using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.IO.Vectors;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Hydraulics;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;
using Utility.Threading;
using Utility.Units;

namespace Ras.Layers;

[DebuggerDisplay("{Count} Meshes")]
public class FlowAreaLayer : GeometryLayerBase<FlowArea>
{
	public static class MessageCodes
	{
		public static readonly MessageClass NoMeshes = new MessageClass("FlowAreaLayer_NoMeshes", MessageSeverity.Error, "Geometry {GeometryName} has 0 meshes.");

		public static readonly MessageClass TablesMissing = new MessageClass("Mesh_MissingTables", MessageSeverity.Warning, "Mesh {MeshName} is missing property tables.");

		public static readonly MessageClass TablesOld = new MessageClass("Mesh_OldTables", MessageSeverity.Warning, "Mesh {MeshName} has old property tables.");

		public static readonly MessageClass FatalErrorComputingTables = new MessageClass("Mesh_FatalErrorComputingTables", MessageSeverity.Error, "Mesh {MeshName} encountered a fatal error. {Exception}");

		public static Message GetTablesMissing(string meshName)
		{
			return TablesMissing.Create(meshName);
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("FlowAreaLayer");

	public const string HDF5GroupName = "/Geometry/2D Flow Areas";

	public const string DefaultMeshLocation = "/Geometry/2D Flow Areas/Mesh";

	public const string DefaultMeshName = "Mesh";

	private readonly EnsureSingleton _propSingleton = new EnsureSingleton();

	private IMeshCollection _meshColl;

	private PropertyTableOptions _tableOptions;

	protected override string HDF5Group => "/Geometry/2D Flow Areas";

	public PropertyTableOptions TableOptions
	{
		get
		{
			if (_tableOptions == null)
			{
				_tableOptions = PropertyTableOptions.Default(Geometry.Project?.Units ?? UnitSystem.USCustomary);
			}
			return _tableOptions;
		}
		set
		{
			_tableOptions = value;
		}
	}

	public FlowAreaLayer(Geometry geom)
		: base(geom)
	{
	}

	protected override void PostLoadFeatures(List<FlowArea> features)
	{
		if (features == null || features.Count == 0)
		{
			return;
		}
		Geometry.UseReader((H5Reader hr) =>
		{
			for (int i = 0; i < features.Count; i++)
			{
				FlowArea flowArea = features[i];
				if (string.IsNullOrEmpty(flowArea.Name))
				{
					_logger.LogWarning("Geometry load error: No FlowArea name for Row# {Row}", i);
				}
				else if (flowArea.Mesh == null)
				{
					_logger.LogInformation("{GeometryName}: Mesh {MeshName} does not exist.", Geometry.Name, flowArea.Name);
				}
				else
				{
					string meshGroup = "/Geometry/2D Flow Areas/" + flowArea.Name;
					if (!MeshReader.ContainsTables(hr, meshGroup, out var _))
					{
						_logger.LogInformation("{GeometryName}: Mesh {MeshName} does not have property tables.", Geometry.Name, flowArea.Name);
					}
					else
					{
						Stopwatch stopwatch = Stopwatch.StartNew();
						flowArea.PropertyTables = MeshReader.ReadTables(flowArea.Mesh, hr, meshGroup);
						string tableSignatureGroupName = MeshReader.TableSignatureGroupName(meshGroup);
						if (!MeshPropertyTableSignature.TryLoad(hr, tableSignatureGroupName, out var signature))
						{
							flowArea.PropertyTableSignature = null;
						}
						else
						{
							flowArea.PropertyTableSignature = signature;
						}
						stopwatch.Stop();
						if (flowArea.PropertyTables != null)
						{
							_logger.LogDebug("{GeometryName} read property tables for {MeshName} in {ElapsedMs}", Geometry.Name, flowArea.Name, stopwatch.Elapsed.TotalMilliseconds);
						}
					}
				}
			}
		});
	}

	protected override void PostSaveInternal(H5Writer hw)
	{
		base.PostSaveInternal(hw);
		for (int i = 0; i < Features.Count; i++)
		{
			FlowArea flowArea = Features[i];
			if (string.IsNullOrEmpty(flowArea.Name))
			{
				_logger.LogWarning("Saving Geometry {GeometryName} error: No FlowArea name for Row# {Row}", Geometry.Name, i);
			}
			else if (flowArea.Mesh == null)
			{
				_logger.LogInformation("{GeometryName}: Mesh {MeshName} does not exist.", Geometry.Name, flowArea.Name);
			}
			else if (flowArea.HasMeshErrors)
			{
				_logger.LogInformation("{GeometryName}: Mesh {MeshName} has errors. It should not have been saved to disk, so neither will property tables.", Geometry.Name, flowArea.Name);
			}
			else if (flowArea.PropertyTables == null)
			{
				_logger.LogInformation("{GeometryName}: Mesh {MeshName} does not have property tables.", Geometry.Name, flowArea.Name);
			}
			else
			{
				string meshGroup = "/Geometry/2D Flow Areas/" + flowArea.Name;
				MeshReader.WriteTables(flowArea.PropertyTables, hw, meshGroup);
				string tableSignatureGroupName = MeshReader.TableSignatureGroupName(meshGroup);
				flowArea.PropertyTableSignature?.Save(hw, tableSignatureGroupName);
			}
		}
	}

	public IMeshCollection AsMeshCollection()
	{
		if (_meshColl == null)
		{
			_meshColl = new MeshFtLayerWrapped<FlowArea>(this, (FlowArea fa) => fa.Mesh, (Mesh m) => new FlowArea(m, ""));
		}
		return _meshColl;
	}

	public void ComputeSavePropertyTables(Terrain terrain, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		throw new NotImplementedException();
	}

	public OperationResult ComputePropertyTables(bool forceRecompute = false, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (Count != 1)
		{
			return OperationResult.Fail("Only a single mesh is supported at this time.");
		}
		using (_propSingleton.Start())
		{
			Geometry.CheckAssociations(out var _, out var tr, out var nv);
			pr.ReportMessage("Associated Terrain: {TerrainName}", tr?.Name ?? "(No Association)");
			pr.ReportMessage("Associated NV Layer: {NVName}", nv?.Name ?? "(No Association)");
			OperationResult operationResult = CanComputePropertyTables();
			if (forceRecompute && !operationResult)
			{
				foreach (Message message in operationResult.Messages)
				{
					pr.ReportMessage("Cannot compute 2D property tables. " + message.EvaluatedMessage);
				}
				return operationResult;
			}
			OperationResult operationResult2 = ComputePropertyTableSignature(out var sig);
			if (!operationResult2)
			{
				return operationResult2;
			}
			if (!forceRecompute)
			{
				OperationResult operationResult3 = PropertyTablesAreCurrent(sig, pr);
				if (operationResult3.Result)
				{
					pr.ReportMessage("Property tables already exist and are up-to-date.");
					if (!operationResult.Result)
					{
						foreach (Message message2 in operationResult.Messages)
						{
							pr.ReportMessage("Note: Cannot compute 2D property tables. " + message2.EvaluatedMessage);
						}
					}
					return true;
				}
				if (!operationResult)
				{
					foreach (Message message3 in operationResult.Messages)
					{
						pr.ReportMessage("Cannot compute 2D property tables. " + message3.EvaluatedMessage);
					}
					OperationResult operationResult4 = true;
					operationResult4.AddFailure(operationResult3);
					operationResult4.AddFailure(operationResult);
					return operationResult4;
				}
			}
			if (pr.CancelRequested)
			{
				return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Computing Property Tables"));
			}
			pr.ReportMessage("Computing Property Tables...");
			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				FlowArea flowArea = base[0];
				for (int i = 0; i < flowArea.Mesh.CellCount; i++)
				{
					if (flowArea.Mesh.CellPolygonArea(i) <= 0.0)
					{
						return OperationResult.Fail(Mesh.MessageCodes.GetBadCellPolygon(i, flowArea.Mesh.CellCenters[i]));
					}
				}
				if (pr.CancelRequested)
				{
					return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Computing Property Tables"));
				}
				OperationResult operationResult5 = MeshPropertyTables.ComputeFrom(flowArea.Mesh, Geometry.AssociatedTerrain, Geometry.AssociatedNValueLayer, TableOptions, out var tables, pr);
				if (pr.CancelRequested)
				{
					return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Computing Property Tables"));
				}
				if (!operationResult5)
				{
					return operationResult5;
				}
				flowArea.PropertyTables = tables;
				flowArea.PropertyTableSignature = sig;
			}
			catch (Exception ex)
			{
				return OperationResult.Fail(GeneralMessageCodes.GetExceptionOccurred("Computing Property Tables (" + base[0].Name + ")", ex));
			}
			stopwatch.Stop();
			sig.DateLastModified = new DateTime(DateTime.Now.Ticks);
			sig.SoftwareVersion = Project.SoftwareVersion;
			sig.ComputeDurationSeconds = stopwatch.Elapsed.TotalSeconds;
			pr.ReportMessage("Computed Property Tables in " + stopwatch.Elapsed.ToPrettyString());
			return true;
		}
	}

	public int EstimateGeometryPrepareCoreCount()
	{
		if (Count == 0)
		{
			return 1;
		}
		int cellCountTerm = base[0]?.CellCount ?? 0;
		if (cellCountTerm <= 0)
		{
			return 1;
		}
		return Math.Min(Math.Max(1, cellCountTerm / 2000), Environment.ProcessorCount);
	}

	public MeshPropertyTables[] AllPropertyTables()
	{
		return Features.Select((FlowArea f) => f.PropertyTables).ToArray();
	}

	public OperationResult ComputePropertyTableSignature(out MeshPropertyTableSignature sig)
	{
		OperationResult operationResult = Geometry.CheckAssociations(out var _, out var tr, out var nv);
		if (!operationResult)
		{
			sig = null;
			return operationResult;
		}
		if (Count != 1)
		{
			sig = null;
			return OperationResult.Fail("Only one mesh supported.");
		}
		Mesh mesh = base[0].Mesh;
		if (mesh == null)
		{
			sig = null;
			return OperationResult.Fail("Mesh not computed/loaded.");
		}
		sig = new MeshPropertyTableSignature();
		sig.MeshCellCount = mesh.CellCount;
		sig.MeshFaceCount = mesh.FaceCount;
		sig.MeshHash = mesh.GetDataHash();
		sig.TerrainSignature = tr.GetSignature();
		sig.NValueSignature = nv.GetSignature();
		return true;
	}

	public OperationResult PropertyTablesAreCurrent(MeshPropertyTableSignature currentTableSig = null, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (Geometry.IsResult)
		{
			return true;
		}
		if (Count != 1)
		{
			return OperationResult.Fail("Only one mesh supported currently.");
		}
		FlowArea flowArea = base[0];
		if (flowArea.PropertyTables == null)
		{
			return OperationResult.Fail(MessageCodes.GetTablesMissing(flowArea.Name));
		}
		MeshPropertyTableSignature propertyTableSignature = flowArea.PropertyTableSignature;
		if (propertyTableSignature == null || !propertyTableSignature.IsLoaded())
		{
			return OperationResult.Fail("No property table signature loaded, can't confirm if tables are correct.");
		}
		if (currentTableSig == null)
		{
			OperationResult operationResult = ComputePropertyTableSignature(out currentTableSig);
			if (!operationResult)
			{
				return operationResult;
			}
		}
		if (flowArea.PropertyTablesPinned)
		{
			pr.ReportMessage("Property tables are pinned - using relaxed validation.");
			return propertyTableSignature.CompareFundamental(currentTableSig, pr);
		}
		return propertyTableSignature.Compare(currentTableSig, pr);
	}

	private OperationResult CanComputePropertyTables()
	{
		OperationResult operationResult = true;
		if (Count == 0)
		{
			operationResult.AddFailure(MessageCodes.NoMeshes.Create(Geometry.Name));
		}
		OperationResult operationResult2 = Geometry.CheckAssociations(out var _, out var _, out var _);
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		return operationResult;
	}

	public void CloneTo(FlowAreaLayer other)
	{
		other.Clear();
		other.TableOptions = TableOptions.Clone();
		using IEnumerator<FlowArea> flowAreas = GetEnumerator();
		while (flowAreas.MoveNext())
		{
			FlowArea current = flowAreas.Current;
			other.Add(current.Clone());
		}
	}
}
