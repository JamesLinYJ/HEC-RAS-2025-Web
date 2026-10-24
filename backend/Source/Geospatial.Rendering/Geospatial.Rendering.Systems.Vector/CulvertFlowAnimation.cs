using System;
using System.Collections.Generic;
using System.Drawing;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems.Vector;

public class CulvertFlowAnimation
{
	private struct FlowParticle
	{
		public double CurrentDistance;

		public long SpawnTick;

		public bool IsActive;
	}

	public static readonly VectorSymbology DefaultSymbology = new VectorSymbology(Color.Black, 1.0, Color.LightBlue)
	{
		PointSize = 12
	};

	public VectorID VectorID;

	private List<FlowParticle> _particles = new List<FlowParticle>();

	private List<Geospatial.Vectors.Point> _pointBuffer = new List<Geospatial.Vectors.Point>();

	private long _lastSpawnTick;

	private long _lastUpdateTick;

	private int _spawnSkipCounter;

	private bool _initialized;

	public double TravelSpeed { get; set; }

	public int SpawnIntervalMs { get; set; } = 500;

	public int SpawnSkipFactor { get; set; } = 1;

	public bool ReverseDirection { get; set; }

	public string DebugName { get; set; }

	public bool IsZeroFlow { get; set; }

	public Polyline BasePolyline { get; }

	public VectorSymbology Symbology { get; }

	public IVectorManager Manager { get; }

	private static long GetTick()
	{
		return DateTime.UtcNow.Ticks;
	}

	public CulvertFlowAnimation(IVectorManager manager, Polyline pline, string name = "")
	{
		Manager = manager;
		BasePolyline = pline;
		Symbology = new VectorSymbology(Color.Black, 1.0, Color.LightBlue)
		{
			PointSize = 12
		};
	}

	public void Dispose()
	{
		Manager.TryDeleteVector(ref VectorID);
		_particles.Clear();
		_pointBuffer = null;
		_initialized = false;
	}

	public void AnimationTimerTick(object sender, object e)
	{
		if (!_initialized)
		{
			_lastSpawnTick = GetTick();
			_lastUpdateTick = GetTick();
			_initialized = true;
		}
		if (IsZeroFlow)
		{
			RenderZeroFlowState();
			return;
		}
		long tick = GetTick();
		double totalSeconds = TimeSpan.FromTicks(tick - _lastUpdateTick).TotalSeconds;
		_lastUpdateTick = tick;
		if (TimeSpan.FromTicks(tick - _lastSpawnTick).TotalMilliseconds >= (double)SpawnIntervalMs)
		{
			_spawnSkipCounter++;
			if (_spawnSkipCounter >= SpawnSkipFactor)
			{
				SpawnParticle(tick);
				_spawnSkipCounter = 0;
			}
			_lastSpawnTick = tick;
		}
		UpdateParticles(totalSeconds);
		RenderParticles();
	}

	private void SpawnParticle(long now)
	{
		FlowParticle flowParticle = new FlowParticle
		{
			CurrentDistance = (ReverseDirection ? BasePolyline.Length : 0.0),
			SpawnTick = now,
			IsActive = true
		};
		_particles.Add(flowParticle);
	}

	private void UpdateParticles(double deltaSeconds)
	{
		double deltaSecondsTimesTravelSpeed = deltaSeconds * TravelSpeed;
		double length = BasePolyline.Length;
		for (int countOffset = _particles.Count - 1; countOffset >= 0; countOffset--)
		{
			FlowParticle particle = _particles[countOffset];
			if (!particle.IsActive)
			{
				_particles.RemoveAt(countOffset);
				continue;
			}
			if (ReverseDirection)
			{
				particle.CurrentDistance -= deltaSecondsTimesTravelSpeed;
				if (particle.CurrentDistance <= 0.0)
				{
					_particles.RemoveAt(countOffset);
					continue;
				}
			}
			else
			{
				particle.CurrentDistance += deltaSecondsTimesTravelSpeed;
				if (particle.CurrentDistance >= length)
				{
					_particles.RemoveAt(countOffset);
					continue;
				}
			}
			_particles[countOffset] = particle;
		}
	}

	private void RenderParticles()
	{
		_pointBuffer.Clear();
		for (int i = 0; i < _particles.Count; i++)
		{
			if (_particles[i].IsActive)
			{
				double currentDistance = _particles[i].CurrentDistance;
				if (!(currentDistance < 0.0) && !(currentDistance > BasePolyline.Length))
				{
					Geospatial.Vectors.Point point = BasePolyline.PointAtDistance(currentDistance).Point;
					_pointBuffer.Add(point);
				}
			}
		}
		if (_pointBuffer.Count > 0)
		{
			Manager.AddOrUpdateVector(ref VectorID, _pointBuffer, Symbology);
		}
		else
		{
			Manager.TryClearVector(ref VectorID);
		}
	}

	private void RenderZeroFlowState()
	{
		double dist = BasePolyline.Length / 2.0;
		Geospatial.Vectors.Point point = BasePolyline.PointAtDistance(dist).Point;
		_pointBuffer.Clear();
		_pointBuffer.Add(point);
		VectorSymbology symb = new VectorSymbology(Symbology.FillColor, 2.0, Color.Transparent)
		{
			PointSize = Symbology.PointSize
		};
		Manager.AddOrUpdateVector(ref VectorID, _pointBuffer, symb);
	}

	public void UpdateParameters(int pointSize, double travelSpeed, bool reverseDirection, bool isZeroFlow, int spawnSkipFactor = 1)
	{
		Symbology.PointSize = pointSize;
		TravelSpeed = travelSpeed;
		ReverseDirection = reverseDirection;
		IsZeroFlow = isZeroFlow;
		SpawnSkipFactor = Math.Max(1, spawnSkipFactor);
		if (travelSpeed > 0.001)
		{
			double basePolylineTravelSpeedTerm = BasePolyline.Length / travelSpeed * 1000.0;
			SpawnIntervalMs = (int)Math.Clamp(basePolylineTravelSpeedTerm / 4.0, 100.0, 2000.0);
		}
		if (isZeroFlow)
		{
			_particles.Clear();
		}
	}
}
