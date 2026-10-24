namespace RasMapperLib;

public class LandCoverCategory
{
	public int ID;

	public string Name;

	public Color Color;

	public LandCoverCategory(int id, Color color, string name)
	{
		ID = id;
		Name = name;
		Color = color;
	}
}
