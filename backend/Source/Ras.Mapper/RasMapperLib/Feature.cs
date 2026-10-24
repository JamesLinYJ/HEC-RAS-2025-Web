using System.Collections.Generic;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public abstract class Feature : IExtent
{
	public abstract string SummaryText { get; }

	public abstract bool HasZ { get; }

	public abstract bool HasM { get; }

	public abstract double MaxX { get; }

	public abstract double MinX { get; }

	public abstract double MaxY { get; }

	public abstract double MinY { get; }

	public abstract double MaxZ { get; }

	public abstract double MinZ { get; }

	public abstract double MaxM { get; }

	public abstract double MinM { get; }

	public abstract int Count { get; }

	public abstract bool MaxMinXYDefined { get; }

	public abstract bool UsesPointSymbol { get; }

	public abstract bool UsesPen { get; }

	public abstract bool UsesBrush { get; }

	public abstract bool UsesSurfaceFill { get; }

	public abstract override string ToString();

	public abstract PointM LabelPoint(FeatureLayer layer);

	public virtual PointM Centroid()
	{
		return new PointM((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);
	}

	public Extent GetExtent()
	{
		return new Extent(this);
	}

	public abstract bool EqualXY(Feature feature);

	protected string PointLabel(bool hasZ, bool hasM)
	{
		if (hasZ && hasM)
		{
			return "XYZM";
		}
		if (hasZ)
		{
			return "XYZ";
		}
		if (hasM)
		{
			return "XYM";
		}
		return "XY";
	}

	public abstract bool Intersects(PointM PointM, double nearDistance);

	public abstract bool Intersects(Extent extent);

	public abstract List<PlotOption> AvailablePlotOptions(bool debugMode);

	public Feature RemoveZAll()
	{
		return SetZAll(double.NaN);
	}

	public Feature RemoveMAll()
	{
		return SetMAll(double.NaN);
	}

	public abstract IList<double> GetZPerVertex();

	public abstract IList<double> GetMPerVertex();

	public abstract Feature SetZPerVertex(IList<double> zs);

	public abstract Feature SetMPerVertex(IList<double> ms);

	public abstract Feature SetZAll(double z);

	public abstract Feature SetMAll(double m);

	public bool MBROverlaps(IExtent ext)
	{
		if (ext == null)
		{
			return false;
		}
		if (double.IsNaN(ext.MinX) || double.IsNaN(ext.MinY))
		{
			return false;
		}
		if (MaxX < ext.MinX)
		{
			return false;
		}
		if (MinX > ext.MaxX)
		{
			return false;
		}
		if (MinY > ext.MaxY)
		{
			return false;
		}
		if (MaxY < ext.MinY)
		{
			return false;
		}
		return true;
	}

	public virtual bool MBROverlaps(Feature Feature)
	{
		if (Feature == null)
		{
			return false;
		}
		if (double.IsNaN(Feature.MinX) || double.IsNaN(Feature.MinY))
		{
			return false;
		}
		if (MaxX < Feature.MinX)
		{
			return false;
		}
		if (MinX > Feature.MaxX)
		{
			return false;
		}
		if (MinY > Feature.MaxY)
		{
			return false;
		}
		if (MaxY < Feature.MinY)
		{
			return false;
		}
		return true;
	}

	public virtual bool MBROverlaps(PointM PointM)
	{
		if (MaxX < PointM.X)
		{
			return false;
		}
		if (MinX > PointM.X)
		{
			return false;
		}
		if (MinY > PointM.Y)
		{
			return false;
		}
		if (MaxY < PointM.Y)
		{
			return false;
		}
		return true;
	}

	public abstract void MD5HashAppend(Hash hash);

	public abstract IEditableFeature GetMutableView();

	public Feature EnsureImmutable()
	{
		if (this is IEditableFeature)
		{
			return ((IEditableFeature)this).Save();
		}
		return this;
	}
}
