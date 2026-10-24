using System;
using System.Threading;
using Geospatial;
using Geospatial.IO.ORM;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Hashing;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Layers.Terrains;

public class FunctionalTerrainInput : ITerrainInput, IDataHash, IDisposable, ISpatialVersion
{
	private static readonly ILogger _logger = LogHelper.GetLogger("FunctionalTerrainInput");

	public const string TypeNameConst = "Functional";

	public const string DefaultExpression = "0";

	private string _expression;

	private Extent _ext;

	private Func<Point, float> _evaluate;

	private string _compileError;

	private FunctionalResampler<float> _res;

	private readonly object _resLock = new object();

	private long _versionXy = 1L;

	[Metadata("Type")]
	public string TypeName => "Functional";

	[Metadata("Name")]
	public string Name { get; set; }

	[Metadata("Expression")]
	public string Expression
	{
		get
		{
			return _expression;
		}
		set
		{
			if (!(_expression == value))
			{
				_expression = value;
				_evaluate = null;
				_compileError = null;
				_res = null;
				Interlocked.Increment(ref _versionXy);
			}
		}
	}

	[Metadata("Valid Extent")]
	public Extent ValidExtent
	{
		get
		{
			return _ext;
		}
		set
		{
			if (!_ext.Equals(value))
			{
				_ext = value;
				_res = null;
				Interlocked.Increment(ref _versionXy);
			}
		}
	}

	public Terrain Terrain { get; private set; }

	public long VersionXY => Interlocked.Read(in _versionXy);

	public FunctionalTerrainInput(Terrain tr, string expression, Extent ext, string name)
	{
		Terrain = tr;
		Expression = expression;
		ValidExtent = ext;
		Name = name;
	}

	private FunctionalTerrainInput()
	{
	}

	public bool TryInit(Terrain terrain, H5Reader hr, string h5Path)
	{
		if (!hr.ExistsAsGroup(h5Path))
		{
			return false;
		}
		Terrain = terrain;
		GeospatialH5Metadata.Read(this, hr, h5Path);
		return true;
	}

	public void Save(H5Writer hw, string h5Path)
	{
		GeospatialH5Metadata.Write(this, hw, h5Path);
	}

	public IResample<float> GetResampler()
	{
		return LazyThreadsafe<FunctionalResampler<float>>.AllocateOnce(ref _res, _resLock, this, (FunctionalTerrainInput model) => model.BuildResampler());
	}

	private FunctionalResampler<float> BuildResampler()
	{
		if (!TryGetEvaluator(out var evaluate))
		{
			return new FunctionalResampler<float>(Extent.NullExtent, -9999f, (Point _) => -9999f);
		}
		return new FunctionalResampler<float>(_ext, -9999f, evaluate);
	}

	private bool TryGetEvaluator(out Func<Point, float> evaluate)
	{
		if (_evaluate != null)
		{
			evaluate = _evaluate;
			return true;
		}
		if (string.IsNullOrWhiteSpace(_expression))
		{
			_compileError = "Expression is empty.";
			evaluate = null;
			return false;
		}
		try
		{
			ExpressionCompiler<double> expressionCompiler = new ExpressionCompiler<double>(_expression, new string[2] { "x", "y" });
			Func<double, double, double> lambda = (Func<double, double, double>)expressionCompiler.CompileLambda();
			_evaluate = (Point pt) => (float)lambda(pt.X, pt.Y);
			_compileError = null;
			evaluate = _evaluate;
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to compile expression on {Layer}: {Expression}", Name, _expression);
			_compileError = ex.Message;
			evaluate = null;
			return false;
		}
	}

	public bool IsValid(out string err)
	{
		if (!TryGetEvaluator(out var _))
		{
			err = _compileError ?? "Invalid expression.";
			return false;
		}
		err = "";
		return true;
	}

	public int GetDataHash()
	{
		return StableHash.Combine(StableHash.Combine(_expression?.GetHashCode() ?? 0, _ext.GetHashCode()), Name?.GetHashCode() ?? 0);
	}

	public void Dispose()
	{
	}

	public void FreezeOne()
	{
	}

	public void ThawOne()
	{
	}
}
