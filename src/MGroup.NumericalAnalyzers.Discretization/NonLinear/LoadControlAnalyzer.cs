using System;
using System.Diagnostics;
using MGroup.MSolve.AnalysisWorkflow;
using MGroup.MSolve.AnalysisWorkflow.Providers;
using MGroup.MSolve.Solution;
using MGroup.MSolve.Solution.LinearSystem;
using MGroup.MSolve.Solution.AlgebraicModel;
using MGroup.NumericalAnalyzers.Logging;
using MGroup.NumericalAnalyzers.NonLinear;
using MGroup.LinearAlgebra;

namespace MGroup.NumericalAnalyzers.Discretization.NonLinear
{
	public class LoadControlAnalyzer : NonLinearAnalyzerBase
	{
		private bool stopIfNotConverged = false;

		/// <summary>
		/// This class solves the linearized geoemtrically nonlinear system of equations according to Newton-Raphson's load control incremental-iterative method.
		/// </summary>
		/// <param name="model">Instance of the model that will be solved</param>
		/// <param name="solver">Instance of the solver that will solve the linear system of equations</param>
		/// <param name="provider">Instance of the problem type to be solved</param>
		/// <param name="subdomainUpdaters">Instance that updates constraints, right-hand-side vector, updates and resets state</param>
		/// <param name="numIncrements">Number of total load increments</param>
		/// <param name="maxIterationsPerIncrement">Number of maximum iterations within a load increment</param>
		/// <param name="numIterationsForMatrixRebuild">Number of iterations for the rebuild of the siffness matrix within a load increment</param>
		/// <param name="residualTolerance">Tolerance for the convergence criterion of the residual forces</param>
		private LoadControlAnalyzer(IAlgebraicModel algebraicModel, ISolver solver, INonLinearProvider provider,
			int numIncrements, int maxIterationsPerIncrement, int numIterationsForMatrixRebuild, double residualTolerance, bool stopIfNotConverged)
			: base(algebraicModel, solver, provider, numIncrements, maxIterationsPerIncrement,
				numIterationsForMatrixRebuild, residualTolerance)
		{
			this.stopIfNotConverged = stopIfNotConverged;
			analysisStatistics.AlgorithmName = "Load control analyzer";
		}


		/// <summary>
		/// Solves the nonlinear equations and calculates the displacements vector.
		/// </summary>
		public override void Solve()
		{
			bool notConverged = false;

			analysisStatistics.NumIterationsRequired = 0;
			InitializeLogs();

			DateTime start = DateTime.Now;
			UpdateInternalVectors();
			for (int increment = 0; increment < numIncrements; increment++)
			{
				double errorNorm = 0;
				ClearIncrementalSolutionVector();
				UpdateRhs(increment);

				double firstError = 0;
				int iteration = 0;
				AnalysisState.newmarkIncrementNumber = increment;
				for (iteration = 0; iteration < maxIterationsPerIncrement; iteration++)
				{
					analysisStatistics.NumIterationsRequired++;

					if (iteration == maxIterationsPerIncrement - 1)
					{
						notConverged = true;
						analysisStatistics.ResidualNormRatioEstimation = errorNorm;
						if (stopIfNotConverged)
						{
							return;
						}
						else
						{
							break;
						}
					}

					if (double.IsNaN(errorNorm))
					{
						notConverged = true;
						analysisStatistics.ResidualNormRatioEstimation = errorNorm;
						if (stopIfNotConverged)
						{
							return;
						}
						else
						{
							break;
						}
					}

					solver.Solve();
					IGlobalVector internalRhsVector = CalculateInternalRhs(increment, iteration);
					double residualNormCurrent = UpdateResidualForcesAndNorm(increment, iteration, internalRhsVector);
					errorNorm = globalRhsNormInitial != 0 ? residualNormCurrent / globalRhsNormInitial : 0;

					if (iteration == 0)
					{
						firstError = errorNorm;
					}

					if (IncrementalDisplacementsLog != null)
					{
						IncrementalDisplacementsLog.StoreDisplacements(uPlusdu);
					}

					if (errorNorm < residualTolerance)
					{
						if (analysisStatistics.ResidualNormRatioEstimation < errorNorm)
						{
							analysisStatistics.ResidualNormRatioEstimation = errorNorm;
						}

						if (IncrementalLog != null)
						{
							IncrementalLog.LogTotalDataForIncrement(increment, iteration, errorNorm, uPlusdu, internalRhsVector);
						}

						break;
					}

					if ((iteration + 1) % numIterationsForMatrixRebuild == 0)
					{
						provider.Reset();
						parentAnalyzer.BuildMatrices();
					}
				}

				if (TotalDisplacementsPerIterationLog != null)
				{
					TotalDisplacementsPerIterationLog.StoreDisplacements(uPlusdu);
				}

				Debug.WriteLine("NR {0}, first error: {1}, exit error: {2}", iteration, firstError, errorNorm);
				SaveMaterialStateAndUpdateSolution();
			}

			analysisStatistics.HasConverged = !notConverged;
			DateTime end = DateTime.Now;
			StoreLogResults(start, end);
		}

		public class Builder : NonLinearAnalyzerBuilderBase
		{
			private bool stopIfNotConverged = true;

			public Builder(IAlgebraicModel algebraicModel, ISolver solver, INonLinearProvider provider, int numIncrements, bool stopIfNotConverged = true)
				: base(algebraicModel, solver, provider, numIncrements)
			{
				MaxIterationsPerIncrement = 1000;
				NumIterationsForMatrixRebuild = 1;
				ResidualTolerance = 1E-3;
				this.stopIfNotConverged = stopIfNotConverged;
			}

			public LoadControlAnalyzer Build() => new LoadControlAnalyzer(algebraicModel, solver, provider,
				numIncrements, maxIterationsPerIncrement, numIterationsForMatrixRebuild, residualTolerance, stopIfNotConverged);
		}
	}
}
