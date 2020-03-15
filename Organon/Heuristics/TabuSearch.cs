﻿using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Osu.Cof.Organon.Heuristics
{
    public class TabuSearch : Heuristic
    {
        public int Iterations { get; set; }
        public int Tenure { get; set; }

        public TabuSearch(Stand stand, OrganonConfiguration organonConfiguration, int harvestPeriods, int planningPeriods, Objective objective)
            :  base(stand, organonConfiguration, harvestPeriods, planningPeriods, objective)
        {
            this.Iterations = stand.TreeRecordCount;
            this.Tenure = (int)(0.3 * stand.TreeRecordCount);

            this.ObjectiveFunctionByIteration = new List<float>(1000)
            {
                this.BestObjectiveFunction
            };
        }

        public override string GetColumnName()
        {
            return "Tabu";
        }

        public override TimeSpan Run()
        {
            if (this.Iterations < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(this.Iterations));
            }
            if (this.Objective.HarvestPeriodSelection != HarvestPeriodSelection.NoneOrLast)
            {
                throw new NotSupportedException(nameof(this.Objective.HarvestPeriodSelection));
            }
            if (this.Tenure < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(this.Tenure));
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            int[,] remainingTabuTenures = new int[this.TreeRecordCount, this.CurrentTrajectory.HarvestPeriods];
            float currentObjectiveFunction = this.BestObjectiveFunction;

            StandTrajectory candidateTrajectory = new StandTrajectory(this.CurrentTrajectory);
            StandTrajectory bestCandidateTrajectory = new StandTrajectory(this.CurrentTrajectory);
            StandTrajectory bestNonTabuCandidateTrajectory = new StandTrajectory(this.CurrentTrajectory);
            //double tenureScalingFactor = ((double)this.Tenure - Constant.RoundToZeroTolerance) / (double)byte.MaxValue;
            for (int neighborhoodEvaluation = 0; neighborhoodEvaluation < this.Iterations; ++neighborhoodEvaluation)
            {
                // evaluate potential moves in neighborhood
                float bestCandidateObjectiveFunction = float.MinValue;
                int bestTreeIndex = -1;
                int bestHarvestPeriod = -1;
                float bestNonTabuCandidateObjectiveFunction = float.MinValue;
                int bestNonTabuUnitIndex = -1;
                int bestNonTabuHarvestPeriod = -1;
                for (int treeIndex = 0; treeIndex < this.TreeRecordCount; ++treeIndex)
                {
                    int currentHarvestPeriod = this.CurrentTrajectory.IndividualTreeSelection[treeIndex];
                    // for (int harvestPeriodIndex = 0; harvestPeriodIndex < this.CurrentTrajectory.HarvestPeriods; ++harvestPeriodIndex)
                    for (int harvestPeriodIndex = 0; harvestPeriodIndex < this.CurrentTrajectory.HarvestPeriods; harvestPeriodIndex += this.CurrentTrajectory.HarvestPeriods - 1)
                    {
                        if (harvestPeriodIndex == currentHarvestPeriod)
                        {
                            continue;
                        }

                        // find objective function for this tree in this period
                        candidateTrajectory.SetTreeSelection(treeIndex, harvestPeriodIndex);
                        candidateTrajectory.Simulate();
                        float candidateObjectiveFunction = this.GetObjectiveFunction(candidateTrajectory);

                        if (candidateObjectiveFunction > bestCandidateObjectiveFunction)
                        {
                            bestCandidateObjectiveFunction = candidateObjectiveFunction;
                            bestCandidateTrajectory.Copy(candidateTrajectory);
                            bestTreeIndex = treeIndex;
                            bestHarvestPeriod = harvestPeriodIndex;
                        }

                        int tabuTenure = remainingTabuTenures[treeIndex, harvestPeriodIndex];
                        if ((tabuTenure == 0) && (candidateObjectiveFunction > bestNonTabuCandidateObjectiveFunction))
                        {
                            bestNonTabuCandidateObjectiveFunction = candidateObjectiveFunction;
                            bestNonTabuCandidateTrajectory.Copy(candidateTrajectory);
                            bestNonTabuUnitIndex = treeIndex;
                            bestNonTabuHarvestPeriod = harvestPeriodIndex;
                        }

                        if (tabuTenure > 0)
                        {
                            remainingTabuTenures[treeIndex, harvestPeriodIndex] = tabuTenure - 1;
                        }

                        // revert candidate trajectory to current trajectory as no mmove has yet been accepted
                        candidateTrajectory.SetTreeSelection(treeIndex, currentHarvestPeriod);
                    }
                }

                // make best move and update tabu table
                // other possibilities: 1) make unit tabu, 2) uncomment stochastic tenure
                if (bestCandidateObjectiveFunction > this.BestObjectiveFunction)
                {
                    // always accept best candidate if it improves upon the best solution
                    currentObjectiveFunction = bestCandidateObjectiveFunction;
                    this.CurrentTrajectory.Copy(bestCandidateTrajectory);

                    remainingTabuTenures[bestTreeIndex, bestHarvestPeriod] = this.Tenure;
                    // remainingTabuTenures[bestUnitIndex, bestHarvestPeriod] = (int)(tenureScalingFactor * this.GetPseudorandomByteAsDouble()) + 1;

                    this.BestObjectiveFunction = bestCandidateObjectiveFunction;
                    this.BestTrajectory.Copy(this.CurrentTrajectory);
                }
                else if (bestNonTabuUnitIndex != -1)
                {
                    // otherwise, accept the best non-tabu move when one exists
                    // Existence is quite likely since (n trees) * (n periods) > tenure in most configurations.
                    currentObjectiveFunction = bestNonTabuCandidateObjectiveFunction;
                    this.CurrentTrajectory.Copy(bestNonTabuCandidateTrajectory);

                    remainingTabuTenures[bestNonTabuUnitIndex, bestNonTabuHarvestPeriod] = this.Tenure;
                    // remainingTabuTenures[bestNonTabuUnitIndex, bestNonTabuHarvestPeriod] = (int)(tenureScalingFactor * this.GetPseudorandomByteAsDouble()) + 1;
                }

                this.ObjectiveFunctionByIteration.Add(currentObjectiveFunction);
            }

            stopwatch.Stop();
            return stopwatch.Elapsed;
        }
    }
}
