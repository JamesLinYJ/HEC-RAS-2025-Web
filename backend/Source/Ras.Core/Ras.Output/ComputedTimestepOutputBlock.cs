using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Geospatial;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Layers;

namespace Ras.Output;

/// <summary>A fixed output view over actual computed mesh snapshots.</summary>
public sealed class ComputedTimestepOutputBlock : IHorizontalOutputBlock
{
	private readonly MeshTimestep[] _timesteps;
	private readonly MeshPropertyTables _tables;

	public string Name => "Base Output";
	public IList<string> ProfileNames { get; }
	public IReadOnlyList<DateTime> Times { get; }
	public Geometry Geometry { get; }

	// This snapshot does not append timesteps after construction.
	public event TimestepAddedEvent TimestepAdded { add { } remove { } }

	public ComputedTimestepOutputBlock(Geometry geometry, IEnumerable<MeshTimestep> timesteps, MeshPropertyTables tables, IReadOnlyList<DateTime> times)
	{
		Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
		_tables = tables ?? throw new ArgumentNullException(nameof(tables));
		_timesteps = timesteps?.ToArray() ?? throw new ArgumentNullException(nameof(timesteps));
		if (geometry.FlowAreaLayer.Count != 1) throw new ArgumentException("Computed snapshots require one source mesh.", nameof(geometry));
		if (times == null || times.Count != _timesteps.Length) throw new ArgumentException("Snapshot times must match the computed profiles.", nameof(times));
		Times = times.ToArray();
		ProfileNames = Times.Select(time => time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).ToList().AsReadOnly();
	}

	public bool TryReadPerMesh(OutputVariable variable, int profile, Memory<float>[] values)
	{
		if ((uint)profile >= (uint)_timesteps.Length) throw new ArgumentOutOfRangeException(nameof(profile));
		if (values == null || values.Length != 1) throw new ArgumentException("One destination per source mesh is required.", nameof(values));
		double[] source;
		if (variable == OutputVariables.Depth) source = _timesteps[profile].WaterState.CellElevation;
		else if (variable == OutputVariables.Velocity) source = _timesteps[profile].WaterState.FaceVelocity;
		else return false;
		if (values[0].IsEmpty) return true;
		if (values[0].Length != source.Length) throw new ArgumentException("Destination length must match the computed mesh elements.", nameof(values));
		Span<float> destination = values[0].Span;
		for (int index = 0; index < source.Length; index++) destination[index] = (float)source[index];
		if (variable == OutputVariables.Depth) _tables.ConvertStageToDepth(values[0]);
		return true;
	}
}
