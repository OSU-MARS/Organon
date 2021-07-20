using Osu.Cof.Ferm.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Osu.Cof.Ferm.Heuristics
{
    // provide a non-template base class to allow PowerShell cmdlets to accept results from any heuristic as a parameter
    public class HeuristicResults
    {
        private readonly int parameterCombinationCount;
        // indices are: parameter combination, first thin, second thin, third thin, rotation length, financial scenario
        // Nullability in multidimensional arrays does not appear supported as of Visual Studio 16.10.1. See remarks in constructor.
        private readonly HeuristicResult[][][][][][] results;

        public FinancialScenarios FinancialScenarios { get; private init; }
        public IList<int> FirstThinPeriods { get; private init; }
        public IList<int> RotationLengths { get; private init; }
        public IList<int> SecondThinPeriods { get; private init; }
        public IList<int> ThirdThinPeriods { get; private init; }

        public GraspReactivity GraspReactivity { get; private init; }
        public IList<HeuristicResultPosition> PositionsEvaluated { get; private init; }

        protected HeuristicResults(int parameterCombinationCount, IList<int> firstThinPeriods, IList<int> secondThinPeriods, IList<int> thirdThinPeriods, IList<int> rotationLengths, FinancialScenarios financialScenarios, int individualPoolSize)
        {
            this.parameterCombinationCount = parameterCombinationCount;
            this.results = new HeuristicResult[parameterCombinationCount][][][][][];

            this.FinancialScenarios = financialScenarios;
            this.FirstThinPeriods = firstThinPeriods;
            this.RotationLengths = rotationLengths;
            this.SecondThinPeriods = secondThinPeriods;
            this.ThirdThinPeriods = thirdThinPeriods;

            this.GraspReactivity = new();
            this.PositionsEvaluated = new List<HeuristicResultPosition>();

            // fill result arrays where applicable
            //   parameter array elements: never null
            //   first thin array elements: never null
            //   second and third thin and planning period thin array elements: maybe null
            //   results in discount rate array elements: never null
            // Jagged arrays which aren't needed are left null. Partly for the minor memory savings, mostly to increase the chances of
            // detecting storage of results to nonsensical locations or other indexing issues.
            for (int parameterIndex = 0; parameterIndex < parameterCombinationCount; ++parameterIndex)
            {
                HeuristicResult[][][][][] parameterCombinationResults = new HeuristicResult[this.FirstThinPeriods.Count][][][][];
                this.results[parameterIndex] = parameterCombinationResults;

                for (int firstThinIndex = 0; firstThinIndex < this.FirstThinPeriods.Count; ++firstThinIndex)
                {
                    HeuristicResult[][][][] firstThinResults = new HeuristicResult[this.SecondThinPeriods.Count][][][];
                    parameterCombinationResults[firstThinIndex] = firstThinResults;

                    int firstThinPeriod = this.FirstThinPeriods[firstThinIndex];
                    for (int secondThinIndex = 0; secondThinIndex < this.SecondThinPeriods.Count; ++secondThinIndex)
                    {
                        int secondThinPeriod = this.SecondThinPeriods[secondThinIndex];
                        if ((secondThinPeriod == Constant.NoThinPeriod) || (secondThinPeriod > firstThinPeriod))
                        {
                            HeuristicResult[][][] secondThinResults = new HeuristicResult[this.ThirdThinPeriods.Count][][];
                            firstThinResults[secondThinIndex] = secondThinResults;

                            int lastOfFirstOrSecondThinPeriod = Math.Max(firstThinPeriod, secondThinPeriod);
                            for (int thirdThinIndex = 0; thirdThinIndex < this.ThirdThinPeriods.Count; ++thirdThinIndex)
                            {
                                int thirdThinPeriod = this.ThirdThinPeriods[thirdThinIndex];
                                if ((thirdThinPeriod == Constant.NoThinPeriod) || (thirdThinPeriod > secondThinPeriod))
                                {
                                    HeuristicResult[][] thirdThinResults = new HeuristicResult[this.RotationLengths.Count][];
                                    secondThinResults[thirdThinIndex] = thirdThinResults;

                                    int lastOfFirstSecondOrThirdThinPeriod = Math.Max(lastOfFirstOrSecondThinPeriod, thirdThinPeriod);
                                    for (int rotationIndex = 0; rotationIndex < this.RotationLengths.Count; ++rotationIndex)
                                    {
                                        int endOfRotationPeriodPeriod = this.RotationLengths[rotationIndex];
                                        if (endOfRotationPeriodPeriod > lastOfFirstSecondOrThirdThinPeriod)
                                        {
                                            HeuristicResult[] rotationLengthResults = new HeuristicResult[this.FinancialScenarios.Count];
                                            thirdThinResults[rotationIndex] = rotationLengthResults;

                                            for (int financialIndex = 0; financialIndex < this.FinancialScenarios.Count; ++financialIndex)
                                            {
                                                rotationLengthResults[financialIndex] = new HeuristicResult(individualPoolSize);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public HeuristicResult this[HeuristicResultPosition position]
        {
            get { return this[position.ParameterIndex, position.FirstThinPeriodIndex, position.SecondThinPeriodIndex, position.ThirdThinPeriodIndex, position.RotationIndex, position.FinancialIndex]; }
        }

        private HeuristicResult this[int parameterIndex, int firstThinPeriodIndex, int secondThinPeriodIndex, int thirdThinPeriodIndex, int rotationIndex, int financialIndex]
        {
            get
            {
                Debug.Assert((parameterIndex >= 0) && (firstThinPeriodIndex >= 0) && (secondThinPeriodIndex >= 0) && (thirdThinPeriodIndex >= 0) && (rotationIndex >= 0) && (financialIndex >= 0));

                HeuristicResult[][][][][] parameterResults = this.results[parameterIndex];
                Debug.Assert(parameterResults != null, "Invalid parameter index " + parameterIndex + ".");
                HeuristicResult[][][][] firstThinResults = parameterResults[firstThinPeriodIndex];
                Debug.Assert(firstThinResults != null, "Invalid first thin index " + firstThinPeriodIndex + ".");
                HeuristicResult[][][] secondThinResults = firstThinResults[secondThinPeriodIndex];
                Debug.Assert(secondThinResults != null, "Invalid second thin index " + secondThinPeriodIndex + ".");
                HeuristicResult[][] thirdThinResults = secondThinResults[thirdThinPeriodIndex];
                Debug.Assert(thirdThinResults != null, "Invalid third thin index " + thirdThinPeriodIndex + ".");
                HeuristicResult[] rotationLengthResults = thirdThinResults[rotationIndex];
                Debug.Assert(rotationLengthResults != null, "Invalid rotation index " + rotationIndex + ".");
                return rotationLengthResults[financialIndex];
            }
            set
            {
                Debug.Assert((parameterIndex >= 0) && (firstThinPeriodIndex >= 0) && (secondThinPeriodIndex >= 0) && (thirdThinPeriodIndex >= 0) && (rotationIndex >= 0) && (financialIndex >= 0));

                HeuristicResult[][][][][] parameterResults = this.results[parameterIndex];
                Debug.Assert(parameterResults != null, "Invalid parameter index " + parameterIndex + ".");
                HeuristicResult[][][][] firstThinResults = parameterResults[firstThinPeriodIndex];
                Debug.Assert(firstThinResults != null, "Invalid first thin index " + firstThinPeriodIndex + ".");
                HeuristicResult[][][] secondThinResults = firstThinResults[secondThinPeriodIndex];
                Debug.Assert(secondThinResults != null, "Invalid second thin index " + secondThinPeriodIndex + ".");
                HeuristicResult[][] thirdThinResults = secondThinResults[thirdThinPeriodIndex];
                Debug.Assert(thirdThinResults != null, "Invalid third thin index " + thirdThinPeriodIndex + ".");
                HeuristicResult[] rotationLengthResults = thirdThinResults[rotationIndex];
                Debug.Assert(rotationLengthResults != null, "Invalid rotation index " + rotationIndex + ".");
                rotationLengthResults[financialIndex] = value; 
            }
        }

        // preferred to adding to PositionsEvaluated directly for argument checking
        public void AddEvaluatedPosition(HeuristicResultPosition position)
        {
            if ((position.ParameterIndex < 0) || (position.ParameterIndex >= this.parameterCombinationCount) ||
                (position.FirstThinPeriodIndex < 0) || (position.FirstThinPeriodIndex >= this.FirstThinPeriods.Count) ||
                (position.SecondThinPeriodIndex < 0) || (position.SecondThinPeriodIndex >= this.SecondThinPeriods.Count) ||
                (position.ThirdThinPeriodIndex < 0) || (position.ThirdThinPeriodIndex >= this.ThirdThinPeriods.Count) ||
                (position.RotationIndex < 0) || (position.RotationIndex >= this.RotationLengths.Count) ||
                (position.FinancialIndex < 0) || (position.FinancialIndex >= this.FinancialScenarios.Count))
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            this.PositionsEvaluated.Add(position);
        }

        public void AssimilateHeuristicRunIntoPosition(Heuristic heuristic, HeuristicPerformanceCounters perfCounters, HeuristicResultPosition position)
        {
            HeuristicResult result = this[position];
            Debug.Assert(result != null);
            result.Distribution.AddSolution(heuristic, position, perfCounters);

            // additions to pools which haven't filled yet aren't informative to GRASP's reactive choice of α as there's no selection
            // pressure
            // One interpretation of this is the pool size sets the minimum amount of information needed to make decisions about the
            // value of solution diversity and objective function improvements.
            bool poolAlreadyFull = result.Pool.IsFull;
            bool acceptedAsEliteSolution = result.Pool.TryAddOrReplace(heuristic, position);
            this.GraspReactivity.Add(heuristic.ConstructionGreediness, acceptedAsEliteSolution && poolAlreadyFull);
        }

        public void GetPoolPerformanceCounters(out int solutionsCached, out int solutionsAccepted, out int solutionsRejected)
        {
            solutionsAccepted = 0;
            solutionsCached = 0;
            solutionsRejected = 0;

            for (int parameterIndex = 0; parameterIndex < this.parameterCombinationCount; ++parameterIndex)
            {
                HeuristicResult[][][][][] parameterCombinationResults = this.results[parameterIndex];
                for (int firstThinIndex = 0; firstThinIndex < parameterCombinationResults.Length; ++firstThinIndex)
                {
                    HeuristicResult[][][][] firstThinResults = parameterCombinationResults[firstThinIndex];
                    for (int secondThinIndex = 0; secondThinIndex < firstThinResults.Length; ++secondThinIndex)
                    {
                        HeuristicResult[][][] secondThinResults = firstThinResults[secondThinIndex];
                        if (secondThinResults != null)
                        {
                            for (int thirdThinIndex = 0; thirdThinIndex < secondThinResults.Length; ++thirdThinIndex)
                            {
                                HeuristicResult[][] thirdThinResults = secondThinResults[thirdThinIndex];
                                if (thirdThinResults != null)
                                {
                                    for (int rotationIndex = 0; rotationIndex < thirdThinResults.Length; ++rotationIndex)
                                    {
                                        HeuristicResult[] rotationLengthResults = thirdThinResults[rotationIndex];
                                        if (rotationLengthResults != null)
                                        {
                                            for (int financialIndex = 0; financialIndex < rotationLengthResults.Length; ++financialIndex)
                                            {
                                                HeuristicResult result = rotationLengthResults[financialIndex];
                                                solutionsAccepted += result.Pool.SolutionsAccepted;
                                                solutionsCached += result.Pool.SolutionsInPool;
                                                solutionsRejected += result.Pool.SolutionsRejected;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // searches among financial scenarios and rotation lengths for solutions assigned to the same set of heuristic parameters and thinnings
        // Looking across parameters would confound heuristic parameter tuning runs by allowing later parameter sets to access solutions obtained
        // by earlier runs. It's assumed parameter set evaluations should always be independent of each other and, therefore, searches are
        // restricted to be within thins, rotation lengths, and financial scenarios.
        public bool TryGetSelfOrFindNearestNeighbor(HeuristicResultPosition position, [NotNullWhen(true)] out HeuristicSolutionPool? neighborOrSelf, [NotNullWhen(true)] out HeuristicResultPosition? neighborOrSelfPosition)
        {
            neighborOrSelf = null;
            neighborOrSelfPosition = null;

            HeuristicResult[][][][][] parameterResults = this.results[position.ParameterIndex];
            Debug.Assert(parameterResults != null);

            // for now, only search within the same number of thinnings
            if (this.FirstThinPeriods[position.FirstThinPeriodIndex] == Constant.NoThinPeriod)
            {
                // no thins
                Debug.Assert(this.SecondThinPeriods[position.SecondThinPeriodIndex] == Constant.NoThinPeriod);
                Debug.Assert(this.ThirdThinPeriods[position.ThirdThinPeriodIndex] == Constant.NoThinPeriod);
                HeuristicResult[][][][] firstThinResults = parameterResults[position.FirstThinPeriodIndex];
                Debug.Assert(firstThinResults != null);
                HeuristicResult[][][] secondThinResults = firstThinResults[position.SecondThinPeriodIndex];
                Debug.Assert(secondThinResults != null);
                HeuristicResult[][] thirdThinResults = secondThinResults[position.ThirdThinPeriodIndex];

                if (HeuristicResults.TryGetSelfOrFindNeighborWithMatchingThinnings(thirdThinResults, position, out neighborOrSelf, out neighborOrSelfPosition))
                {
                    return true;
                }
            }
            else if (this.SecondThinPeriods[position.SecondThinPeriodIndex] == Constant.NoThinPeriod)
            {
                // one thin
                BreadthFirstEnumerator<HeuristicResult[][][][]> thinEnumerator = new(parameterResults, position.FirstThinPeriodIndex);
                while (thinEnumerator.MoveNext())
                {
                    // for now, exclude positions with different numbers of thins from neighborhood
                    if (this.FirstThinPeriods[thinEnumerator.Index] == Constant.NoThinPeriod)
                    {
                        continue;
                    }
                    Debug.Assert(this.ThirdThinPeriods[position.ThirdThinPeriodIndex] == Constant.NoThinPeriod);

                    // position also has one thin, so search among rotation lengths and financial scenarios
                    HeuristicResult[][][] secondThinResults = thinEnumerator.Current[position.SecondThinPeriodIndex];
                    Debug.Assert(secondThinResults != null);
                    HeuristicResult[][] thirdThinResults = secondThinResults[position.ThirdThinPeriodIndex];
                    if (HeuristicResults.TryGetSelfOrFindNeighborWithMatchingThinnings(thirdThinResults, position, out neighborOrSelf, out neighborOrSelfPosition))
                    {
                        return true;
                    }
                }
            }
            else if (this.ThirdThinPeriods[position.ThirdThinPeriodIndex] == Constant.NoThinPeriod)
            {
                // two thins
                BreadthFirstEnumerator2D<HeuristicResult[][][]> thinEnumerator = new(parameterResults, position.FirstThinPeriodIndex, position.SecondThinPeriodIndex);
                while (thinEnumerator.MoveNext())
                {
                    // for now, exclude positions with different numbers of thins from neighborhood
                    if ((this.FirstThinPeriods[thinEnumerator.IndexX] == Constant.NoThinPeriod) ||
                        (this.SecondThinPeriods[thinEnumerator.IndexY] == Constant.NoThinPeriod))
                    {
                        continue;
                    }
                    Debug.Assert(this.ThirdThinPeriods[position.ThirdThinPeriodIndex] == Constant.NoThinPeriod);

                    // position also has two thins, so search among rotation lengths and financial scenarios
                    HeuristicResult[][] thirdThinResults = thinEnumerator.Current[position.ThirdThinPeriodIndex];
                    if (HeuristicResults.TryGetSelfOrFindNeighborWithMatchingThinnings(thirdThinResults, position, out neighborOrSelf, out neighborOrSelfPosition))
                    {
                        return true;
                    }
                }
            }
            else
            {
                // three thins
                BreadthFirstEnumerator3D<HeuristicResult[][]> thinEnumerator = new(parameterResults, position.FirstThinPeriodIndex, position.SecondThinPeriodIndex, position.ThirdThinPeriodIndex);
                while (thinEnumerator.MoveNext())
                {
                    // for now, exclude positions with different numbers of thins from neighborhood
                    if ((this.FirstThinPeriods[thinEnumerator.IndexX] == Constant.NoThinPeriod) ||
                        (this.SecondThinPeriods[thinEnumerator.IndexY] == Constant.NoThinPeriod) ||
                        (this.ThirdThinPeriods[thinEnumerator.IndexZ] == Constant.NoThinPeriod))
                    {
                        continue;
                    }

                    // position also has three thins, so search among rotation lengths and financial scenarios
                    if (HeuristicResults.TryGetSelfOrFindNeighborWithMatchingThinnings(thinEnumerator.Current, position, out neighborOrSelf, out neighborOrSelfPosition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetSelfOrFindNeighborWithMatchingThinnings(HeuristicResult[][] thirdThinResults, HeuristicResultPosition position, [NotNullWhen(true)] out HeuristicSolutionPool? neighborOrSelf, [NotNullWhen(true)] out HeuristicResultPosition? neighborOrSelfPosition)
        {
            neighborOrSelf = null;
            neighborOrSelfPosition = null;

            Debug.Assert((thirdThinResults != null) && (thirdThinResults.Length > 0));

            // for now, assume solutions at different rotation lengths are closer to each other than solutions at different financial indices
            // This is likely true for discount rate sweeps. It's likely false for financial uncertainty sweeps.
            BreadthFirstEnumerator2D<HeuristicResult> rotationAndFinancialEnumerator = new(thirdThinResults, position.RotationIndex, position.FinancialIndex, BreadthFirstEnumerator2D.XFirst);
            while (rotationAndFinancialEnumerator.MoveNext())
            {
                neighborOrSelf = rotationAndFinancialEnumerator.Current.Pool;
                if (neighborOrSelf.SolutionsInPool > 0)
                {
                    neighborOrSelfPosition = new(position)
                    {
                        RotationIndex = rotationAndFinancialEnumerator.IndexX,
                        FinancialIndex = rotationAndFinancialEnumerator.IndexY
                    };
                    return true;
                }
            }

            return false;
        }
    }

    public class HeuristicResults<TParameters> : HeuristicResults where TParameters : HeuristicParameters
    {
        public IList<TParameters> ParameterCombinations { get; private init; }

        public HeuristicResults(IList<TParameters> parameterCombinations, IList<int> firstThinPeriods, IList<int> secondThinPeriods, IList<int> thirdThinPeriods, IList<int> rotationLengths, FinancialScenarios financialScenarios, int individualSolutionPoolSize)
            : base(parameterCombinations.Count, firstThinPeriods, secondThinPeriods, thirdThinPeriods, rotationLengths, financialScenarios, individualSolutionPoolSize)
        {
            this.ParameterCombinations = parameterCombinations;
        }
    }
}
