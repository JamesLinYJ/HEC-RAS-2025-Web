using System;
using System.Collections.Generic;
using System.Drawing;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems.Vector;

public class PolylineVectorAnimation
{
	public static readonly VectorSymbology DefaultAnimationSymb = new VectorSymbology(Color.Black, 1.0, Color.Blue)
	{
		PointSize = 12
	};

	public VectorID VectorID;

	public float SizeRatio { get; set; } = 1f;

	public double PointSeparation { get; set; } = 600.0;

	public double TravelSpeed { get; set; } = 600.0;

	public Polyline BasePolyline { get; }

	public List<Geospatial.Vectors.Point> Points { get; private set; }

	public long StartTick { get; } = DateTime.Now.Ticks;

	public VectorSymbology Symbology { get; } = new VectorSymbology(Color.Black, 1.0, Color.LightBlue)
	{
		PointSize = 12
	};

	public IVectorManager Manager { get; }

	public PolylineVectorAnimation(IVectorManager manager, Polyline pline)
	{
		Manager = manager;
		BasePolyline = pline;
	}

	private void Init()
	{
		int count = (int)Math.Floor(BasePolyline.Length / PointSeparation);
		Points = new List<Geospatial.Vectors.Point>();
		for (int i = 0; i < count; i++)
		{
			Points.Add(default);
		}
	}

	public void Dispose()
	{
		Manager.TryDeleteVector(ref VectorID);
		Points = null;
	}

	public void AnimationTimerTick(object sender, object e)
	{
		if ((long)VectorID == 0L)
		{
			Init();
		}
		double totalSecondsTimesTravelSpeed = TimeSpan.FromTicks(DateTime.Now.Ticks - StartTick).TotalSeconds * TravelSpeed;
		double length = BasePolyline.Length;
		for (int i = 0; i < Points.Count; i++)
		{
			double dist = (totalSecondsTimesTravelSpeed + PointSeparation * (double)i) % length;
			Points[i] = BasePolyline.PointAtDistance(dist).Point;
		}
		Manager.AddOrUpdateVector(ref VectorID, Points, Symbology);
	}
}
