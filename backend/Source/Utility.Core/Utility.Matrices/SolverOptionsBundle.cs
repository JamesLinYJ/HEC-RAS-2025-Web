namespace Utility.Matrices;

public sealed record SolverOptionsBundle
{
	public PardisoOptions PardisoOpts;

	public FgmresOptions FgmresOpts;

	public PcgNativeOptions PcgNativeOpts;

	public PcgManagedOptions PcgManagedOpts;

	public SsorOptions SsorOpts;

	public PgmresOptions PgmresOpts;

	public GaussOptions GaussOpts;
}
