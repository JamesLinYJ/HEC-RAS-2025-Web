namespace Geospatial;

public interface IDiscreteTimestepsWithEvent : IDiscreteTimesteps
{
	event TimestepAddedEvent TimestepAdded;
}
