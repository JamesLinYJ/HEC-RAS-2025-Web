namespace RasMapperLib;

public struct RowColM
{
	public int Row;

	public int Col;

	public RowColM(int r, int c)
	{
		this = default;
		Row = r;
		Col = c;
	}

	public void Assign(int r, int c)
	{
		Row = r;
		Col = c;
	}

	public bool Equal(RowColM rc)
	{
		if (Row == rc.Row && Col == rc.Col)
		{
			return true;
		}
		return false;
	}
}
