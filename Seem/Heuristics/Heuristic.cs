using Osu.Cof.Ferm.Organon;
using Osu.Cof.Ferm.Tree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Osu.Cof.Ferm.Heuristics
{
    public abstract class Heuristic : PseudorandomizingTask
    {
        public OrganonStandTrajectory?[,] BestTrajectoryByRotationAndScenario { get; private init; }
        public float ConstructionGreediness { get; protected set; }
        public OrganonStandTrajectory CurrentTrajectory { get; private init; } // could be protected but left public for test access
        public FinancialValueTrajectory FinancialValue { get; protected set; }
        public RunParameters RunParameters { get; private init; }

        protected Heuristic(OrganonStand stand, RunParameters runParameters, bool evaluatesAcrossRotationsAndDiscountRates)
        {
            if ((runParameters.RotationLengths.Count < 1) || (runParameters.Financial.Count < 1))
            {
                throw new ArgumentOutOfRangeException(nameof(runParameters));
            }

            int financialScenarioCapacity = 1;
            int lastSimulationPeriod = runParameters.MaximizeForPlanningPeriod;
            int rotationLengthCapacity = 1;
            if (evaluatesAcrossRotationsAndDiscountRates)
            {
                financialScenarioCapacity = runParameters.Financial.Count;
                lastSimulationPeriod = runParameters.RotationLengths.Max();
                rotationLengthCapacity = runParameters.RotationLengths.Count;
            }

            this.BestTrajectoryByRotationAndScenario = new OrganonStandTrajectory[rotationLengthCapacity, financialScenarioCapacity];
            OrganonStandTrajectory? trajectoryToCloneToCurrent = null;
            for (int rotationIndex = 0; rotationIndex < rotationLengthCapacity; ++rotationIndex)
            {
                if (evaluatesAcrossRotationsAndDiscountRates)
                {
                    int endOfRotationPeriod = runParameters.RotationLengths[rotationIndex];
                    if (endOfRotationPeriod <= runParameters.LastThinPeriod)
                    {
                        continue; // not a valid rotation length because it doesn't include the last thinning scheduled
                    }
                }

                for (int financialIndex = 0; financialIndex < financialScenarioCapacity; ++financialIndex)
                {
                    OrganonStandTrajectory trajectory = new(stand, runParameters.OrganonConfiguration, runParameters.TreeVolume, lastSimulationPeriod)
                    {
                        Heuristic = this
                    };
                    trajectory.Treatments.CopyFrom(runParameters.Treatments);

                    this.BestTrajectoryByRotationAndScenario[rotationIndex, financialIndex] = trajectory;
                    trajectoryToCloneToCurrent = trajectory;
                }
            }
            // depending on the thinnings specified this.BestTrajectoryByRotationAndRate[Constant.HeuristicDefault.RotationIndex, Constant.HeuristicDefault.FinancialIndex]
            // can be null
            if (trajectoryToCloneToCurrent == null)
            {
                throw new ArgumentOutOfRangeException(nameof(runParameters), "No valid rotation lengths found.");
            }

            this.CurrentTrajectory = new OrganonStandTrajectory(trajectoryToCloneToCurrent);
            this.CurrentTrajectory.Name += "Current";

            this.ConstructionGreediness = Constant.Grasp.NoConstruction;
            this.FinancialValue = new(rotationLengthCapacity, financialScenarioCapacity);
            this.RunParameters = runParameters;
        }

        protected void CopyTreeGrowthToBestTrajectory(OrganonStandTrajectory trajectory)
        {
            this.CopyTreeGrowthToBestTrajectory(trajectory, Constant.HeuristicDefault.RotationIndex, Constant.HeuristicDefault.FinancialIndex);
        }

        protected void CopyTreeGrowthToBestTrajectory(OrganonStandTrajectory trajectory, int rotationIndex, int financialIndex)
        {
            OrganonStandTrajectory bestTrajectory = this.GetBestTrajectory(rotationIndex, financialIndex);
            bestTrajectory.CopyTreeGrowthFrom(trajectory);
        }

        protected OrganonStandTrajectory GetBestTrajectory(int rotationIndex, int financialIndex)
        {
            OrganonStandTrajectory? bestTrajectory = this.BestTrajectoryByRotationAndScenario[rotationIndex, financialIndex];
            Debug.Assert(bestTrajectory != null);
            return bestTrajectory;
        }

        public OrganonStandTrajectory GetBestTrajectoryWithDefaulting(HeuristicResultPosition position)
        {
            if (this.BestTrajectoryByRotationAndScenario.Length == 1)
            {
                return this.GetBestTrajectory(Constant.HeuristicDefault.RotationIndex, Constant.HeuristicDefault.FinancialIndex);
            }
            return this.GetBestTrajectory(position.RotationIndex, position.FinancialIndex);
        }

        public abstract string GetName();

        public float GetFinancialValue(StandTrajectory trajectory, int financialIndex) // public for test code access
        {
            return this.GetFinancialValue(trajectory, trajectory.PlanningPeriods - 1, financialIndex);
        }

        protected float GetFinancialValue(StandTrajectory trajectory, int endOfRotationPeriodIndex, int financialIndex)
        {
            Debug.Assert(trajectory.PeriodLengthInYears > 0);

            // find objective function value
            // Volume objective functions are in m³/ha or MBF/ac.
            float financialValue;
            if ((this.RunParameters.TimberObjective == TimberObjective.LandExpectationValue) ||
                (this.RunParameters.TimberObjective == TimberObjective.NetPresentValue))
            {
                // net present value of first rotation
                financialValue = this.RunParameters.Financial.GetNetPresentValue(trajectory, financialIndex, endOfRotationPeriodIndex);

                if (this.RunParameters.TimberObjective == TimberObjective.LandExpectationValue)
                {
                    int rotationLengthInYears = trajectory.GetEndOfPeriodAge(endOfRotationPeriodIndex);
                    float presentToFutureConversionFactor = this.RunParameters.Financial.GetAppreciationFactor(financialIndex, rotationLengthInYears);
                    float landExpectationValue = presentToFutureConversionFactor * financialValue / (presentToFutureConversionFactor - 1.0F);
                    financialValue = landExpectationValue;
                }

                // convert from US$/ha to USk$/ha
                financialValue *= 0.001F;
            }
            else if (this.RunParameters.TimberObjective == TimberObjective.ScribnerVolume)
            {
                // TODO: move this out of financial objective calculations, left here as legacy support for now 
                // direct volume addition
                financialValue = 0.0F;
                for (int periodIndex = 1; periodIndex < trajectory.PlanningPeriods; ++periodIndex)
                {
                    financialValue += trajectory.GetTotalScribnerVolumeThinned(periodIndex);
                }
                financialValue += trajectory.GetTotalStandingScribnerVolume(endOfRotationPeriodIndex);
            }
            else
            {
                throw new NotSupportedException("Unhandled timber objective " + this.RunParameters.TimberObjective + ".");
            }

            return financialValue;
        }

        protected float[,] GetFinancialValueByRotationAndScenario(StandTrajectory trajectory)
        {
            float[,] financialValueByScenario = new float[this.RunParameters.RotationLengths.Count, this.RunParameters.Financial.Count];
            for (int rotationIndex = 0; rotationIndex < this.RunParameters.RotationLengths.Count; ++rotationIndex)
            {
                int endOfRotationPeriod = this.RunParameters.RotationLengths[rotationIndex];
                for (int financialIndex = 0; financialIndex < this.RunParameters.Financial.Count; ++financialIndex)
                {
                    financialValueByScenario[rotationIndex, financialIndex] = this.GetFinancialValue(trajectory, endOfRotationPeriod, financialIndex);
                }
            }
            return financialValueByScenario;
        }

        protected int GetOneOptCandidateRandom(int currentHarvestPeriod, IList<int> thinningPeriods)
        {
            Debug.Assert((thinningPeriods.Count == 2) || (thinningPeriods.Count == 3));
            if (thinningPeriods.Count == 2)
            {
                return currentHarvestPeriod == thinningPeriods[0] ? thinningPeriods[1] : thinningPeriods[0];
            }

            bool incrementIndex = this.Pseudorandom.GetPseudorandomByteAsProbability() < 0.5F;
            if (currentHarvestPeriod == thinningPeriods[0])
            {
                return incrementIndex ? thinningPeriods[2] : thinningPeriods[1];
            }
            else if (currentHarvestPeriod == thinningPeriods[1])
            {
                return incrementIndex ? thinningPeriods[0] : thinningPeriods[2];
            }
            else
            {
                Debug.Assert(currentHarvestPeriod == thinningPeriods[2]);
                return incrementIndex ? thinningPeriods[0] : thinningPeriods[1];
            }
        }

        public virtual IHeuristicMoveLog? GetMoveLog()
        {
            return null;
        }

        public abstract HeuristicParameters GetParameters();
        public abstract HeuristicPerformanceCounters Run(HeuristicResultPosition position, HeuristicResults results);
    }

    public abstract class Heuristic<TParameters> : Heuristic where TParameters : HeuristicParameters
    {
        public TParameters HeuristicParameters { get; private init; }

        public Heuristic(OrganonStand stand, TParameters heuristicParameters, RunParameters runParameters, bool evaluatesAcrossRotationsAndDiscountRates)
            : base(stand, runParameters, evaluatesAcrossRotationsAndDiscountRates)
        {
            if ((heuristicParameters.MinimumConstructionGreediness < Constant.Grasp.FullyRandomConstructionForMaximization) || (heuristicParameters.MinimumConstructionGreediness > Constant.Grasp.FullyGreedyConstructionForMaximization))
            {
                throw new ArgumentOutOfRangeException(nameof(heuristicParameters), "Construction greediness is not between 0 and 1, inclusive.");
            }
            if ((heuristicParameters.InitialThinningProbability < 0.0F) || (heuristicParameters.InitialThinningProbability > 1.0F))
            {
                throw new ArgumentOutOfRangeException(nameof(heuristicParameters), "Probability of thinning an individual tree is not between 0 and 1, inclusive.");
            }

            this.HeuristicParameters = heuristicParameters;
        }

        // exposed as a distinct method for AutocorrelatedWalk
        protected int ConstructTreeSelection(float constructionGreediness)
        {
            if ((constructionGreediness <Constant.Grasp.FullyRandomConstructionForMaximization) || (constructionGreediness > Constant.Grasp.FullyGreedyConstructionForMaximization))
            {
                throw new ArgumentOutOfRangeException(nameof(constructionGreediness));
            }

            // check if there is a thinning to randomize
            IList<int> thinningPeriods = this.CurrentTrajectory.Treatments.GetThinningPeriods();
            if (thinningPeriods.Count == 0)
            {
                // ensure no trees are selected for thinning since no thinning is specified
                // If needed, checking can be done for conflict between removing trees from thinning and construction greediness. At least for now, the 
                // scheduling of thins is treated as an override to greediness.
                this.CurrentTrajectory.DeselectAllTrees();
                return 0;
            }

            if (this.HeuristicParameters.MinimumConstructionGreediness == Constant.Grasp.FullyGreedyConstructionForMaximization)
            {
                // nothing to do
                // Could return sooner but it seems best not to bypass argument checking.
                return 0;
            }

            // randomize tree selection at level indicated by constructionGreediness
            int initialTreeRecordCount = this.CurrentTrajectory.GetInitialTreeRecordCount();
            float thinIndexScalingFactor = (thinningPeriods.Count - Constant.RoundTowardsZeroTolerance) / this.HeuristicParameters.InitialThinningProbability;
            int treeSelectionsRandomized = 0;
            for (int uncompactedTreeIndex = 0; uncompactedTreeIndex < initialTreeRecordCount; ++uncompactedTreeIndex)
            {
                if (constructionGreediness != Constant.Grasp.FullyRandomConstructionForMaximization)
                {
                    float modificationProbability = this.Pseudorandom.GetPseudorandomByteAsProbability();
                    if (modificationProbability < constructionGreediness)
                    {
                        continue;
                    }
                }

                int thinningPeriod = Constant.NoHarvestPeriod;
                float harvestProbability = this.Pseudorandom.GetPseudorandomByteAsProbability();
                if (harvestProbability < this.HeuristicParameters.InitialThinningProbability)
                {
                    // probability falls into the harvest fraction, for now choose equally among available harvest periods
                    // TODO: support unequal harvest period probabilities
                    int periodIndex = (int)(thinIndexScalingFactor * harvestProbability);
                    thinningPeriod = thinningPeriods[periodIndex];
                }
                this.CurrentTrajectory.SetTreeSelection(uncompactedTreeIndex, thinningPeriod);
                ++treeSelectionsRandomized; // debatable: should randomizations which don't change a tree's harvest period not be counted?
            }

            return treeSelectionsRandomized;
        }

        protected int ConstructTreeSelection(HeuristicResultPosition position, HeuristicResults results)
        {
            // attempt to find an existing solution with the same set of thinning timings
            // If found, the existing solution's discount rate and number of planning periods may differ.
            if (results.TryGetSelfOrFindNearestNeighbor(position, out HeuristicSolutionPool? existingSolutions, out HeuristicResultPosition? positionOfExistingSolutions))
            {
                Debug.Assert(existingSolutions.SolutionsInPool > 0);
                OrganonStandTrajectory eliteSolution = existingSolutions.GetEliteSolution(position);
                this.CurrentTrajectory.CopyTreeGrowthFrom(eliteSolution);

                // assuming an existing elite solution at this position has reached first order convergence, must randomize at least two trees
                // to potentially shift to some other domain of attraction
                // The actual number of trees randomized is nondeterministic, the construction process may not necessarily randomize trees to
                // a different harvest timing, and the size of the domain of attraction is unknown. Therefore, set the minimum randomization to
                // be several trees more than the minimum of two.
                int initialTreeRecordCount = this.CurrentTrajectory.GetInitialTreeRecordCount();
                float maximumConstructionGreediness = (initialTreeRecordCount - Constant.Grasp.MinimumTreesRandomized) / (float)initialTreeRecordCount;
                if (maximumConstructionGreediness <= this.HeuristicParameters.MinimumConstructionGreediness)
                {
                    // fall back to permitting fully greedy construction if only a few trees are present or when the minimum greediness is
                    // especially high
                    maximumConstructionGreediness = Constant.Grasp.FullyGreedyConstructionForMaximization;
                }
                if (position == positionOfExistingSolutions)
                {
                    // if one elite solution is available, begin this search with semi-greeding construction
                    this.ConstructionGreediness = results.GraspReactivity.GetConstructionGreediness(this.HeuristicParameters.MinimumConstructionGreediness, maximumConstructionGreediness);
                    // TODO: if two or more elite solutions are available, within position path relinking is possible
                    // this.ConstructionGreediness = Constant.Grasp.FullyRandomConstructionForMaximization;
                }
                else
                {
                    // default to minimal randomization since no solution has yet been accepted at this position
                    // Fully greedy construction is suitable the first time an existing elite solution is evaluated at a new position.
                    // However, within the current codebase it's not known how many other threads may have already initiated searches at this
                    // position, may initiate searches before this search completes, and which existing elite solutions from may currenty be
                    // under evaluation. For now, minimal randomization is therefore applied to reduce the probability of performing duplicate
                    // searches.
                    this.ConstructionGreediness = maximumConstructionGreediness;
                    // this.ConstructionGreediness = this.HeuristicParameters.MinimumConstructionGreediness;
                    // this.ConstructionGreediness = Constant.Grasp.FullyGreedyConstructionForMaximization;
                    // this.ConstructionGreediness = Constant.Grasp.FullyRandomConstructionForMaximization;

                    // sync prescription to new thinning timings
                    if (position.FirstThinPeriodIndex != positionOfExistingSolutions.FirstThinPeriodIndex)
                    {
                        int currentThinPeriod = results.FirstThinPeriods[position.FirstThinPeriodIndex];
                        int existingSolutionThinPeriod = results.FirstThinPeriods[positionOfExistingSolutions.FirstThinPeriodIndex];
                        this.CurrentTrajectory.ChangeThinningPeriod(existingSolutionThinPeriod, currentThinPeriod);
                    }
                    if (position.SecondThinPeriodIndex != positionOfExistingSolutions.SecondThinPeriodIndex)
                    {
                        int currentThinPeriod = results.SecondThinPeriods[position.SecondThinPeriodIndex];
                        int existingSolutionThinPeriod = results.SecondThinPeriods[positionOfExistingSolutions.SecondThinPeriodIndex];
                        this.CurrentTrajectory.ChangeThinningPeriod(existingSolutionThinPeriod, currentThinPeriod);
                    }
                    if (position.ThirdThinPeriodIndex != positionOfExistingSolutions.ThirdThinPeriodIndex)
                    {
                        int currentThinPeriod = results.ThirdThinPeriods[position.ThirdThinPeriodIndex];
                        int existingSolutionThinPeriod = results.ThirdThinPeriods[positionOfExistingSolutions.ThirdThinPeriodIndex];
                        this.CurrentTrajectory.ChangeThinningPeriod(existingSolutionThinPeriod, currentThinPeriod);
                    }
                }
            }
            else
            {
                // default to fully random construction if no solution has yet been found for this combination of thinnings
                this.ConstructionGreediness = Constant.Grasp.FullyRandomConstructionForMaximization;
            }

            return this.ConstructTreeSelection(this.ConstructionGreediness);
        }

        protected virtual float EvaluateInitialSelection(HeuristicResultPosition position, int moveCapacity, HeuristicPerformanceCounters perfCounters)
        {
            this.FinancialValue.SetMoveCapacity(moveCapacity);

            perfCounters.GrowthModelTimesteps += this.CurrentTrajectory.Simulate();
            this.BestTrajectoryByRotationAndScenario[Constant.HeuristicDefault.RotationIndex, Constant.HeuristicDefault.FinancialIndex]!.CopyTreeGrowthFrom(this.CurrentTrajectory);

            float financialValue = this.GetFinancialValue(this.CurrentTrajectory, position.FinancialIndex);
            this.FinancialValue.AddMove(Constant.HeuristicDefault.RotationIndex, Constant.HeuristicDefault.FinancialIndex, financialValue, financialValue);
            return financialValue;
        }

        public override HeuristicParameters GetParameters()
        {
            return this.HeuristicParameters;
        }
    }
}
