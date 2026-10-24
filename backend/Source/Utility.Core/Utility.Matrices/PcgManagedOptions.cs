namespace Utility.Matrices;

public sealed record PcgManagedOptions(MatrixConvergence<double> MatrixConv = null, MatrixSolver<double>.ScalingType ScalingType = MatrixSolver<double>.ScalingType.Symmetric);
