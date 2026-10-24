using System.Linq;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class GeometryLayerExtensions
{
	public class VirtualGeometryWrapper : IGeometryLayer, IVirtualFeatureLayer
	{
		private IGeometryLayer _geometryLayerInstance;

		private IVirtualFeatureLayer _virtualFeatureLayerInstance;

		public RASGeometry Geometry => _geometryLayerInstance.Geometry;

		public string HDFGroupName => _geometryLayerInstance.HDFGroupName;

		public FeatureLayer Layer => _virtualFeatureLayerInstance.Layer;

		public FeatureLayer SourceLayer => _virtualFeatureLayerInstance.SourceLayer;

		public string CountAttributeName => _virtualFeatureLayerInstance.CountAttributeName;

		public void HDFLoadFeatureTable(H5Reader hr)
		{
			_geometryLayerInstance.HDFLoadFeatureTable(hr);
		}

		void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
		{
			//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
			this.HDFLoadFeatureTable(hr);
		}

		public void HDFSaveFeatureTable(H5Writer hw)
		{
			_geometryLayerInstance.HDFSaveFeatureTable(hw);
		}

		void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
		{
			//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
			this.HDFSaveFeatureTable(hw);
		}

		public VirtualGeometryWrapper(object localValue)
		{
			_geometryLayerInstance = (IGeometryLayer)localValue;
			_virtualFeatureLayerInstance = (IVirtualFeatureLayer)localValue;
		}
	}

	public static TerrainLayer GetAssociatedTerrainLayer(this IGeometryLayer lyr)
	{
		return lyr.Geometry?.Terrain;
	}

	public static bool HasFeatures(this IGeometryLayer lyr, H5Reader h5r)
	{
		if (!string.IsNullOrEmpty(lyr.HDFGroupName))
		{
			if (!h5r.ExistsAsGroup(lyr.HDFGroupName))
			{
				return false;
			}
			string hdfGroupNameLyrTerm = lyr.HDFGroupName + "/Attributes";
			if (h5r.ExistsAsDataset(hdfGroupNameLyrTerm))
			{
				long[] datasetDimensions = h5r.GetDatasetDimensions(hdfGroupNameLyrTerm);
				return datasetDimensions.Count() > 0 && datasetDimensions[0] > 0;
			}
		}
		return true;
	}

	public static bool HasFeatures<T>(this T lyr, string attributeName, H5Reader h5r) where T : IGeometryLayer, IVirtualFeatureLayer
	{
		if (!string.IsNullOrEmpty(lyr.HDFGroupName))
		{
			if (!h5r.ExistsAsGroup(lyr.HDFGroupName))
			{
				return false;
			}
			if (h5r.ExistsAsAttribute(lyr.HDFGroupName, attributeName))
			{
				int ordinal = default;
				h5r.ReadAttributeScalar(lyr.HDFGroupName, attributeName, ref ordinal);
				return ordinal > 0;
			}
			return true;
		}
		return false;
	}
}
