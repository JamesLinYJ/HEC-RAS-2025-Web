using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Mapping;

public class OutputModes
{
	private string _description;

	private string _longDescription;

	private bool _isDynamic;

	private bool _isVector;

	private bool _isPoint;

	private bool _useAlternateTerrain;

	public static readonly OutputModes DynamicSurface = new OutputModes(dynamic: true, "Dynamic Surface", "Map results are generated on-the-fly for the current view.", isVector: false, useAlternateTerrain: false, isPoint: false);

	public static readonly OutputModes DynamicPoint = new OutputModes(dynamic: true, "Dynamic Point", "Map results are generated on-the-fly at the specified input point locations.", isVector: true, useAlternateTerrain: false, isPoint: true);

	public static readonly OutputModes StoredDefaultTerrain = new OutputModes(dynamic: false, "Stored Current Terrain", "Map results are computed using the associtated Terrain and stored to disk.", isVector: false, useAlternateTerrain: false, isPoint: false);

	public static readonly OutputModes StoredSpecifiedTerrain = new OutputModes(dynamic: false, "Stored Specified Terrain", "Map results are computed using the specified Terrain and stored to disk.", isVector: false, useAlternateTerrain: true, isPoint: false);

	public static readonly OutputModes StoredPoint = new OutputModes(dynamic: false, "Stored Point", "Map results are computed at the specified input point locations and stored to disk.", isVector: true, useAlternateTerrain: false, isPoint: true);

	public static readonly OutputModes StoredPolygonSpecifiedDepth = new OutputModes(dynamic: false, "Stored Polygon Specified Depth", "Polygon Boundary Map is computed at specified value and stored to disk.", isVector: true, useAlternateTerrain: false, isPoint: false);

	public static readonly OutputModes StoredPolygonBands = new OutputModes(dynamic: false, "Stored Polygon Bands", "Contour Bands are computed at user-specified values and stored to disk.", isVector: true, useAlternateTerrain: false, isPoint: false);

	public static readonly OutputModes Unknown = new OutputModes(dynamic: false, "N/A", "Unknown Output Mode", isVector: false, useAlternateTerrain: false, isPoint: false);

	public static readonly List<OutputModes> AvailableTypes = new List<OutputModes>(new OutputModes[7] { DynamicSurface, DynamicPoint, StoredDefaultTerrain, StoredSpecifiedTerrain, StoredPoint, StoredPolygonSpecifiedDepth, StoredPolygonBands });

	public string Description => _description;

	public string LongDescription => _longDescription;

	public bool IsDynamic => _isDynamic;

	public bool IsStored => !_isDynamic;

	public bool IsVector => _isVector;

	public bool IsRaster => !_isVector;

	public bool IsPoint => _isPoint;

	public bool UseAlternateTerrain => _useAlternateTerrain;

	private OutputModes(bool dynamic, string desc, string longDesc, bool isVector, bool useAlternateTerrain, bool isPoint)
	{
		_description = desc;
		_longDescription = longDesc;
		_isDynamic = dynamic;
		_isVector = isVector;
		_useAlternateTerrain = useAlternateTerrain;
		_isPoint = isPoint;
	}

	public override bool Equals(object obj) => ReferenceEquals(this, obj);

	public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

	public static bool operator ==(OutputModes a, OutputModes b)
	{
		if ((object)a == null || (object)b == null)
		{
			return false;
		}
		return a.Equals(b);
	}

	public static bool operator !=(OutputModes a, OutputModes b)
	{
		return !(a == b);
	}

	public static OutputModes LoadFromDescription(string description)
	{
		if (Operators.CompareString(description, "Stored Polygon Zero Depth", TextCompare: false) == 0)
		{
			return StoredPolygonSpecifiedDepth;
		}
		OutputModes outputModes = AvailableTypes.FirstOrDefault([SpecialName] (OutputModes mt) => Operators.CompareString(mt.Description, description, TextCompare: false) == 0);
		if ((object)outputModes == null)
		{
			outputModes = Unknown;
		}
		return outputModes;
	}
}
