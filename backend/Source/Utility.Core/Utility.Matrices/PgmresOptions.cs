namespace Utility.Matrices;

public sealed record PgmresOptions(int MaxInnerIterations = 10, MatrixConvergence<double> MatrixConv = null, MatrixSolver<double>.ScalingType ScalingType = MatrixSolver<double>.ScalingType.Symmetric);
