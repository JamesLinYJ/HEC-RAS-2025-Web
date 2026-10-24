namespace Utility.Resources;

public struct NamedResource
{
	public string ResourceName { get; }

	public NamedResource(string resourceName)
	{
		ResourceName = resourceName;
	}

	public static implicit operator string(NamedResource resource)
	{
		return resource.ResourceName;
	}

	public static implicit operator NamedResource(string resourceName)
	{
		return new NamedResource(resourceName);
	}
}
