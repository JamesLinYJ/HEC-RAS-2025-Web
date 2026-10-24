using System;
using System.Diagnostics.CodeAnalysis;

namespace Utility.Matrices;

public static class MatrixSolverFactory
{
	public static readonly string NativeMatrixSolverLibName = "Utility.Math.x86.dll";

	public const string PardisoNativeSolverFullyQualifiedName = "Utility.Math.x86.Pardiso";

	public const string FgmresNativeSolverFullyQualifiedName = "Utility.Math.x86.FGMRES";

	public const string PcgNativeSolverFullyQualifiedName = "Utility.Math.x86.CG";

	public static bool ForceUseInternalSolver { get; set; } = false;

	public static IMatrixSolver<double> Create()
	{
		return Create(new SolverRequest(new SolverPolicy(SolverKind.Pardiso, new _003C_003Ez__ReadOnlySingleElementList<SolverKind>(SolverKind.PcgManaged)), new LinearSystemInfo(IsSparse: true, IsSymmetric: false, IsPositiveDefinite: false, 1000), new SolverOptionsBundle()));
	}

	public static IMatrixSolver<double> Create(SolverRequest request)
	{
		SolverKind kind = ResolveSolverKind(request);
		IMatrixSolver<double> matrixSolver = (IMatrixSolver<double>)Activator.CreateInstance(ResolveSolverType(kind));
		ApplyOptions(kind, matrixSolver, request.Options);
		return matrixSolver;
	}

	private static SolverKind ResolveSolverKind(SolverRequest request)
	{
		if (ForceUseInternalSolver)
		{
			if (request.SystemInfo != null && request.SystemInfo.IsSymmetric)
			{
				return SolverKind.PcgManaged;
			}
			return SolverKind.Pgmres;
		}
		if (TryResolveKind(request.Policy.Primary, out var resolvedType))
		{
			return request.Policy.Primary;
		}
		if (request.Policy.ThrowIfUnavailable)
		{
			throw new NotSupportedException($"Requested solver '{request.Policy.Primary}' is unavailable on this platform or could not be loaded.");
		}
		if (request.Policy.FallbackOrder != null)
		{
			foreach (SolverKind fallbackOrder in request.Policy.FallbackOrder)
			{
				if (TryResolveKind(fallbackOrder, out resolvedType))
				{
					return fallbackOrder;
				}
			}
		}
		return SolverKind.PcgManaged;
	}

	private static bool TryResolveKind(SolverKind kind, out Type resolvedType)
	{
		resolvedType = null;
		if (!IsNativeKind(kind))
		{
			try
			{
				resolvedType = ResolveManagedSolverType(kind);
				return true;
			}
			catch
			{
				resolvedType = null;
				return false;
			}
		}
		try
		{
			resolvedType = TryLoadNativeSolverType(kind);
			return (object)resolvedType != null;
		}
		catch
		{
			resolvedType = null;
			return false;
		}
	}

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private static Type ResolveSolverType(SolverKind kind)
	{
		if (IsNativeKind(kind))
		{
			return TryLoadNativeSolverType(kind) ?? throw new NotSupportedException($"Native solver '{kind}' could not be loaded.");
		}
		return ResolveManagedSolverType(kind);
	}

	private static bool IsNativeKind(SolverKind kind)
	{
		if ((uint)kind <= 2u)
		{
			return true;
		}
		return false;
	}

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, "Utility.Math.x86.Pardiso", "Utility.Math.x86")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private static Type TryLoadNativeSolverType(SolverKind kind)
	{
        if (!OperatingSystem.IsWindows())
		{
			return null;
		}
        return kind switch
		{
            SolverKind.Pardiso => Type.GetType("Utility.Math.x86.Pardiso, Utility.Math.x86", throwOnError: false),
            SolverKind.Fgmres or SolverKind.PcgNative => null,
			_ => throw new ArgumentOutOfRangeException("kind", kind, "Not a native solver kind."), 
		};
	}

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
	private static Type ResolveManagedSolverType(SolverKind kind)
	{
		return kind switch
		{
			SolverKind.PcgManaged => typeof(PCG<double>), 
			SolverKind.Ssor => typeof(SSOR<double>), 
			SolverKind.Pgmres => typeof(PGMRES<double>), 
			SolverKind.Gauss => typeof(Gauss<double>), 
			_ => throw new NotSupportedException($"Managed solver kind '{kind}' does not have a mapped type."), 
		};
	}

	private static void ApplyOptions(SolverKind kind, IMatrixSolver<double> solver, SolverOptionsBundle bundle)
	{
		object kindPardisoTerm = kind switch
		{
			SolverKind.Pardiso => (object)bundle.PardisoOpts, 
			SolverKind.Fgmres => bundle.FgmresOpts, 
			SolverKind.PcgNative => bundle.PcgNativeOpts, 
			SolverKind.PcgManaged => bundle.PcgManagedOpts, 
			SolverKind.Ssor => bundle.SsorOpts, 
			SolverKind.Pgmres => bundle.PgmresOpts, 
			SolverKind.Gauss => bundle.GaussOpts, 
			_ => null, 
		};
		if (kindPardisoTerm != null)
		{
			solver.Configure(kindPardisoTerm);
		}
	}
}
