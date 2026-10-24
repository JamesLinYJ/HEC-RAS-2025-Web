using Geospatial.Vectors;

namespace Geospatial.Rendering.ParticleTracking;

public class Particle
{
	public int Age { get; set; }

	public Point Position { get; set; }

	public Particle(Point position)
	{
		Age = 0;
		Position = position;
	}

	public Particle(int age, Point position)
	{
		Age = age;
		Position = position;
	}
}
