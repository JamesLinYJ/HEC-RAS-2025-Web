using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.IO.Vectors;
using Geospatial.Vectors.Meshes;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Hydraulics;
using Utility.Logging;
using Utility.Threading;
using Utility.Units;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Meshes")]
public class ClassicFlowAreaLayer : ClassicGeometryLayerBase<FlowArea>
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

	public ClassicFlowAreaLayer(ClassicGeometry geom)
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

	public MeshPropertyTables[] AllPropertyTables()
	{
		return Features.Select((FlowArea f) => f.PropertyTables).ToArray();
	}
}
