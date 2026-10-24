using System;
using System.Collections.Generic;
using System.IO;
using H5Assist.VB;
using RasMapperLib.Utilities;
using Utility.Extensions;

namespace RasMapperLib;

public class GeometryAssociation
{
	public delegate void AssociationChangedEventHandler(Layer before, Layer after);

	public const string sTerrainAssociationName = "Terrain";

	public const string sTerrainFilename = "Terrain Filename";

	public const string sTerrainFileDate = "Terrain File Date";

	public const string sTerrainLayername = "Terrain Layername";

	public const string sLandCoverAssociationName = "Land Cover";

	public const string sLandCoverFilename = "Land Cover Filename";

	public const string sLandCoverFileDate = "Land Cover File Date";

	public const string sLandCoverLayername = "Land Cover Layername";

	public const string sInfiltrationAssociationName = "Infiltration";

	public const string sInfiltrationFilename = "Infiltration Filename";

	public const string sInfiltrationFileDate = "Infiltration File Date";

	public const string sInfiltrationLayername = "Infiltration Layername";

	public const string sPercentImperviousAssociationName = "Percent Impervious";

	public const string sSedimentSoilsAssociationName = "Sediment Bed Material";

	public static readonly string[] AllKnownAssociationNames = new string[4] { "Land Cover", "Infiltration", "Percent Impervious", "Sediment Bed Material" };

	private readonly string _associationName;

	private readonly Func<GeometryAssociation, Layer> _resolveLayer;

	private readonly string _pathRelative;

	private string _relativeFilename;

	private string _layerName;

	private Layer _resolvedLayer;

	private string _fileDateStringFromDisk;

	private string _layerDateModStringFromDisk;

	public string LayerName => _layerName;

	public string AssociationName => _associationName;

	public DateTime FileDate
	{
		get
		{
			string absoluteFilename = AbsoluteFilename;
			if (string.IsNullOrEmpty(absoluteFilename))
			{
				return DateTime.MinValue;
			}
			if (File.Exists(absoluteFilename))
			{
				return File.GetLastWriteTime(absoluteFilename);
			}
			return DateTime.MinValue;
		}
	}

	public string FileDateString
	{
		get
		{
			DateTime fileDate = FileDate;
			if (DateTime.Compare(fileDate, DateTime.MinValue) == 0)
			{
				return "";
			}
			return DSS.ConvertToDSSDateTime(fileDate);
		}
	}

	public string RelativeFilename => _relativeFilename;

	public string AbsoluteFilename
	{
		get
		{
			if (string.IsNullOrEmpty(_relativeFilename))
			{
				return string.Empty;
			}
			if (string.IsNullOrEmpty(_pathRelative))
			{
				return string.Empty;
			}
			return RasMapperLib.Utilities.Path.MakeAbsolute(_relativeFilename, _pathRelative);
		}
	}

	public string SavedFileDateString => _fileDateStringFromDisk;

	public string SavedLayerDateModifiedString => _layerDateModStringFromDisk;

	public Layer Layer
	{
		get
		{
			if (_resolvedLayer != null)
			{
				return _resolvedLayer;
			}
			Layer resolvedLayer = _resolvedLayer;
			_resolvedLayer = _resolveLayer?.Invoke(this);
			if (_resolvedLayer != null)
			{
				_layerName = _resolvedLayer.Name;
			}
			if (resolvedLayer != null || _resolvedLayer != null)
			{
				AssociationChanged?.Invoke(resolvedLayer, _resolvedLayer);
			}
			return _resolvedLayer;
		}
	}

	public bool HasAssociation => !string.IsNullOrEmpty(_relativeFilename);

	public event AssociationChangedEventHandler AssociationChanged;

	public GeometryAssociation(string associationName, string relativePathName, Func<GeometryAssociation, Layer> resolveLayer)
	{
		_associationName = associationName;
		_pathRelative = relativePathName;
		_resolveLayer = resolveLayer;
	}

	public void ChangeAssociation(Layer newLayer)
	{
		_fileDateStringFromDisk = "";
		Layer resolvedLayer = _resolvedLayer;
		if (newLayer == null)
		{
			_relativeFilename = "";
			_layerName = "";
			_resolvedLayer = null;
		}
		else
		{
			_relativeFilename = RasMapperLib.Utilities.Path.MakeRelative(newLayer.SourceFilename, _pathRelative);
			_layerName = newLayer.Name;
			_resolvedLayer = newLayer;
		}
		AssociationChanged?.Invoke(resolvedLayer, _resolvedLayer);
	}

	public void SaveAssociation(H5Writer hw, string group)
	{
		if (!HasAssociation)
		{
			DeleteAssociationAttributes(hw, group);
			return;
		}
		string fileDateString = FileDateString;
		string layerDateLastModifiedStringTerm = Layer?.DateLastModifiedString();
		hw.WriteAttribute(group, FileNameAttribute(_associationName), _relativeFilename);
		if (!string.IsNullOrEmpty(fileDateString))
		{
			hw.WriteAttribute(group, FileDateModifiedAttribute(_associationName), fileDateString);
		}
		if (!string.IsNullOrEmpty(layerDateLastModifiedStringTerm))
		{
			hw.WriteAttribute(group, LayerDateModifiedAttribute(_associationName), layerDateLastModifiedStringTerm);
		}
		if (!string.IsNullOrEmpty(_layerName))
		{
			hw.WriteAttribute(group, LayerNameAttribute(_associationName), _layerName);
		}
	}

	public void LoadAssociation(H5Reader hr, string group, bool ensureIsHDF5Filename = true)
	{
		hr.TryReadAttributeScalar(group, FileNameAttribute(_associationName), ref _relativeFilename);
		if (ensureIsHDF5Filename)
		{
			_relativeFilename = EnsureHDFFilename(_relativeFilename);
		}
		hr.TryReadAttributeScalar(group, FileDateModifiedAttribute(_associationName), ref _fileDateStringFromDisk);
		hr.TryReadAttributeScalar(group, LayerDateModifiedAttribute(_associationName), ref _layerDateModStringFromDisk);
		hr.TryReadAttributeScalar(group, LayerNameAttribute(_associationName), ref _layerName);
	}

	public void LoadAssociation(H5Reader hr, string group, Func<string, string> filenameModifier)
	{
		hr.TryReadAttributeScalar(group, FileNameAttribute(_associationName), ref _relativeFilename);
		if (!string.IsNullOrEmpty(_relativeFilename))
		{
			_relativeFilename = filenameModifier(_relativeFilename);
		}
		hr.TryReadAttributeScalar(group, FileDateModifiedAttribute(_associationName), ref _fileDateStringFromDisk);
		hr.TryReadAttributeScalar(group, LayerDateModifiedAttribute(_associationName), ref _layerDateModStringFromDisk);
		hr.TryReadAttributeScalar(group, LayerNameAttribute(_associationName), ref _layerName);
	}

	private string EnsureHDFFilename(string fn)
	{
		if (string.IsNullOrEmpty(fn))
		{
			return fn;
		}
		if (fn.RoughlyEndsWith(".hdf") || fn.RoughlyEndsWith(".h5"))
		{
			return fn;
		}
		return System.IO.Path.ChangeExtension(fn, "hdf");
	}

	public void DeleteAssociationAttributes(H5Writer hw, string group)
	{
		hw.DeleteAttribute(group, FileNameAttribute(_associationName));
		hw.DeleteAttribute(group, FileDateModifiedAttribute(_associationName));
		hw.DeleteAttribute(group, LayerDateModifiedAttribute(_associationName));
		hw.DeleteAttribute(group, LayerNameAttribute(_associationName));
	}

	public static void UpdateAssociations(H5Writer hw, string prevFilename, string groupname, IList<string> associationNames)
	{
		if (associationNames.IsNullOrEmpty())
		{
			return;
		}
		string filename = hw.Filename;
		foreach (string associationName in associationNames)
		{
			string attributeName = FileNameAttribute(associationName);
			string tryReadStringAttributeScalar = hw.TryReadStringAttributeScalar(groupname, attributeName);
			if (!string.IsNullOrEmpty(tryReadStringAttributeScalar))
			{
				tryReadStringAttributeScalar = RasMapperLib.Utilities.Path.MakeRelative(RasMapperLib.Utilities.Path.MakeAbsolute(tryReadStringAttributeScalar, prevFilename), filename);
				hw.WriteAttribute(groupname, attributeName, tryReadStringAttributeScalar);
			}
		}
	}

	private static string FileNameAttribute(string prefix)
	{
		return prefix + " Filename";
	}

	private static string FileDateModifiedAttribute(string prefix)
	{
		return prefix + " File Date";
	}

	private static string LayerDateModifiedAttribute(string prefix)
	{
		return prefix + " Date Last Modified";
	}

	private static string LayerNameAttribute(string prefix)
	{
		return prefix + " Layername";
	}
}
