namespace Ras.Layers;

public class PlanAssociation
{
	private Geometry _geom;

	private BoundaryCondition _bc;

	public Project Project { get; }

	public string GeometryName { get; private set; }

	public string BoundaryConditionName { get; private set; }

	public Geometry Geometry
	{
		get
		{
			return _geom;
		}
		set
		{
			_geom = value;
			GeometryName = _geom?.Name ?? "";
		}
	}

	public BoundaryCondition BoundaryCondition
	{
		get
		{
			return _bc;
		}
		set
		{
			_bc = value;
			if (_bc != null)
			{
				BoundaryConditionName = _bc.Name ?? "";
			}
		}
	}

	private PlanAssociation(Project prj)
	{
		Project = prj;
	}

	public PlanAssociation(Project prj, string geomName, string bcName)
	{
		Project = prj;
		GeometryName = geomName;
		BoundaryConditionName = bcName;
		if (!string.IsNullOrEmpty(geomName))
		{
			_geom = Project.FindGeometry(geomName);
		}
		if (!string.IsNullOrEmpty(bcName))
		{
			_bc = Project.FindBoundaryConditions(bcName);
		}
	}

	public PlanAssociation(Geometry geom, BoundaryCondition bc)
	{
		Geometry = geom;
		BoundaryCondition = bc;
	}

	public bool IsDefault()
	{
		if (string.IsNullOrEmpty(GeometryName))
		{
			return string.IsNullOrEmpty(BoundaryConditionName);
		}
		return false;
	}

	public void AlertGeometryRenamed(string name)
	{
		GeometryName = name;
	}

	public void AlertGeometryRemoved()
	{
		_geom = null;
	}

	public void AlertBoundaryConditionRenamed(string name)
	{
		BoundaryConditionName = name;
	}

	public static PlanAssociation CreateNew(Project prj)
	{
		return new PlanAssociation(prj);
	}
}
