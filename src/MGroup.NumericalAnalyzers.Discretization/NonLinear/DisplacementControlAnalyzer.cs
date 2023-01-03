using System;
using System.Diagnostics;
using MGroup.MSolve.AnalysisWorkflow;
using MGroup.MSolve.AnalysisWorkflow.Providers;
using MGroup.MSolve.Solution;
using MGroup.MSolve.Solution.LinearSystem;
using MGroup.MSolve.Solution.AlgebraicModel;
using MGroup.NumericalAnalyzers.NonLinear;
using MGroup.NumericalAnalyzers.Logging;

namespace MGroup.NumericalAnalyzers.Discretization.NonLinear
{
	/// <summary>
	/// This class solves the nonlinear system of equations using the displacement control method
	/// </summary>
	public class DisplacementControlAnalyzer : NonLinearAnalyzerBase
	{
		/// <summary>
		/// This class solves the linearized geometrically nonlinear system of equations according to displacement control incremental-iterative method.
		/// This only works if there are no nodal loads or any loading condition other than prescribed displacements.
		/// </summary>
		/// <param name="model">Instance of the model that will be solved</param>
		/// <param name="solver">Instance of the solver that will solve the linear system of equations</param>
		/// <param name="provider">Instance of the problem type to be solved</param>
		/// <param name="subdomainUpdaters">Instance that updates constraints, right-hand-side vector, updates and resets state</param>
		/// <param name="numIncrements">Number of total load increments</param>
		/// <param name="maxIterationsPerIncrement">Number of maximum iterations within a load increment</param>
		/// <param name="numIterationsForMatrixRebuild">Number of iterations for the rebuild of the siffness matrix within a load increment</param>
		/// <param name="residualTolerance">Tolerance for the convergence criterion of the residual forces</param>
		private DisplacementControlAnalyzer(IAlgebraicModel algebraicModel, ISolver solver, INonLinearProvider provider,
			int numIncrements, int maxIterationsPerIncrement, int numIterationsForMatrixRebuild, double residualTolerance)
			: base(algebraicModel, solver, provider, numIncrements, maxIterationsPerIncrement,
				numIterationsForMatrixRebuild, residualTolerance)
		{
		}

		/// <summary>
		/// Solves the nonlinear equations and calculates the displacements vector
		/// </summary>
		public override void Solve()
		{
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
				for (iteration = 0; iteration < maxIterationsPerIncrement; iteration++)
				{
					AddEquivalentNodalLoadsToRHS(increment, iteration);
					solver.Solve();

					IGlobalVector internalRhsVector = CalculateInternalRhs(increment, iteration);
					errorNorm = UpdateResidualForcesAndNorm(increment, internalRhsVector);

					if (iteration == 0)
					{
						firstError = errorNorm;
					}

					if (TotalDisplacementsPerIterationLog != null)
					{
						TotalDisplacementsPerIterationLog.StoreDisplacements(uPlusdu);
					}

					if (errorNorm < residualTolerance)
					{
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
				Debug.WriteLine("NR {0}, first error: {1}, exit error: {2}", iteration, firstError, errorNorm);
				SaveMaterialStateAndUpdateSolution();
			}

			DateTime end = DateTime.Now;
			StoreLogResults(start, end);
		}

		private void AddEquivalentNodalLoadsToRHS(int iteration, int iteration1)
		{
			if (iteration != 0)
			{
				return;
			}

			//double scalingFactor = 1;
			IGlobalVector equivalentNodalLoads = algebraicModel.CreateZeroVector();
			algebraicModel.AddToGlobalVector(provider.EnumerateEquivalentNeumannBoundaryConditions, equivalentNodalLoads);
			solver.LinearSystem.RhsVector.SubtractIntoThis(equivalentNodalLoads);
		}

		public class Builder : NonLinearAnalyzerBuilderBase
		{
			public Builder(IAlgebraicModel algebraicModel, ISolver solver, INonLinearProvider provider, int numIncrements)
				: base(algebraicModel, solver, provider, numIncrements)
			{
				MaxIterationsPerIncrement = 1000;
				NumIterationsForMatrixRebuild = 1;
				ResidualTolerance = 1E-3;
			}

			public DisplacementControlAnalyzer Build()
			{
				return new DisplacementControlAnalyzer(algebraicModel, solver, provider, 
					numIncrements, maxIterationsPerIncrement, numIterationsForMatrixRebuild, residualTolerance);
			}
		}
	}
}
