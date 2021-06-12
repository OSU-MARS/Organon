﻿using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Osu.Cof.Ferm.Heuristics
{
    public class HeuristicSolutionPool : SolutionPool
    {
        private readonly Heuristic?[] eliteSolutions;
        private int lowestEliteIndex;
        private float lowestEliteFinancialValue;

        public Heuristic? Low { get; private set; }
        public Heuristic? High { get; private set; }

        public HeuristicSolutionPool(int capacity)
            : base(capacity)
        {
            this.eliteSolutions = new Heuristic[capacity];
            this.lowestEliteIndex = 0;
            this.lowestEliteFinancialValue = Single.MinValue;

            this.Low = null;
            this.High = null;
        }

        public OrganonStandTrajectory GetEliteSolution(HeuristicResultPosition position)
        {
            if (this.SolutionsInPool < 1)
            {
                throw new InvalidOperationException();
            }
            if (this.SolutionsInPool == 1)
            {
                return this.eliteSolutions[0]!.GetBestTrajectoryWithDefaulting(position);
            }

            float solutionIndexScalingFactor = (this.SolutionsInPool - Constant.RoundTowardsZeroTolerance) / byte.MaxValue;
            int solutionIndex = (int)(solutionIndexScalingFactor * this.Pseudorandom.GetPseudorandomByteAsFloat());
            return this.eliteSolutions[solutionIndex]!.GetBestTrajectoryWithDefaulting(position);
        }

        private void Replace(int replacementIndex, Heuristic heuristic, HeuristicResultPosition position, int[] neighborDistances, int nearestNeighborIndex)
        {
            this.eliteSolutions[replacementIndex] = heuristic;
            this.UpdateNearestNeighborDistances(replacementIndex, neighborDistances, nearestNeighborIndex);

            // update index and objective of lowest solution in pool
            this.lowestEliteFinancialValue = Single.MaxValue;
            for (int index = 0; index < this.eliteSolutions.Length; ++index)
            {
                float solutionOFinancialValue = this.eliteSolutions[index]!.FinancialValue.GetHighestValueWithDefaulting(position);
                if (solutionOFinancialValue < this.lowestEliteFinancialValue)
                {
                    this.lowestEliteFinancialValue = solutionOFinancialValue;
                    this.lowestEliteIndex = index;
                }
            }
        }

        public bool TryAddOrReplace(Heuristic heuristic, HeuristicResultPosition position)
        {
            // pool is empty (first time TryAddOrReplace() is called)
            if (this.SolutionsInPool == 0)
            {
                this.eliteSolutions[0] = heuristic;
                this.lowestEliteIndex = 0;
                this.lowestEliteFinancialValue = heuristic.FinancialValue.GetHighestValueWithDefaulting(position);
                this.SolutionsAccepted = 1;
                this.SolutionsInPool = 1;

                this.Low = heuristic;
                this.High = heuristic;
                return true;
            }

            bool solutionAccepted;
            if (this.SolutionsInPool < this.PoolCapacity)
            {
                // calculate distances to solutions already in pool
                int[] distancesToSolutionsInPool = new int[this.SolutionsInPool];
                SortedDictionary<FiaCode, int[]> heuristicTreeSelection = heuristic.GetBestTrajectoryWithDefaulting(position).IndividualTreeSelectionBySpecies;
                int nearestNeighborDistance = Int32.MaxValue;
                int nearestNeighborIndex = -1;
                for (int solutionIndex = 0; solutionIndex < this.SolutionsInPool; ++solutionIndex)
                {
                    SortedDictionary<FiaCode, int[]> eliteTreeSelection = this.eliteSolutions[solutionIndex]!.GetBestTrajectoryWithDefaulting(position).IndividualTreeSelectionBySpecies;
                    int distanceToSolution = SolutionPool.GetHammingDistance(heuristicTreeSelection, eliteTreeSelection);
                    if (distanceToSolution == 0)
                    {
                        // this solution is already in the pool and therefore doesn't need to be added
                        Debug.Assert((heuristic.FinancialValue.GetHighestValueWithDefaulting(position) >= this.Low!.FinancialValue.GetHighestValueWithDefaulting(position)) && 
                                     (heuristic.FinancialValue.GetHighestValueWithDefaulting(position) <= this.High!.FinancialValue.GetHighestValueWithDefaulting(position)));
                        return false;
                    }

                    distancesToSolutionsInPool[solutionIndex] = distanceToSolution;
                    if (distanceToSolution < nearestNeighborDistance)
                    {
                        nearestNeighborDistance = distanceToSolution;
                        nearestNeighborIndex = solutionIndex;
                    }
                }

                // add solution since pool is still filling
                this.eliteSolutions[this.SolutionsInPool] = heuristic;
                float heuristicFinancialValue = heuristic.FinancialValue.GetHighestValueWithDefaulting(position);
                if (heuristicFinancialValue < this.lowestEliteFinancialValue)
                {
                    this.lowestEliteFinancialValue = heuristicFinancialValue;
                    this.lowestEliteIndex = this.SolutionsInPool;
                }

                this.UpdateNearestNeighborDistances(this.SolutionsInPool, distancesToSolutionsInPool, nearestNeighborIndex);

                ++this.SolutionsInPool;
                solutionAccepted = true;
            }
            else
            {
                solutionAccepted = this.TryReplaceByDiversityOrObjective(heuristic, position);
            }

            if (solutionAccepted)
            {
                float heuristicFinancialValue = heuristic.FinancialValue.GetHighestValueWithDefaulting(position);
                if (heuristicFinancialValue > this.High!.FinancialValue.GetHighestValueWithDefaulting(position))
                {
                    this.High = heuristic;
                }
                else if (heuristicFinancialValue < this.Low!.FinancialValue.GetHighestValueWithDefaulting(position))
                {
                    this.Low = heuristic;
                }
                ++this.SolutionsAccepted;

                Debug.Assert(heuristicFinancialValue >= this.Low!.FinancialValue.GetHighestValueWithDefaulting(position));
            }
            else
            {
                ++this.SolutionsRejected;
            }
            Debug.Assert(heuristic.FinancialValue.GetHighestValueWithDefaulting(position) <= this.High!.FinancialValue.GetHighestValueWithDefaulting(position));

            return solutionAccepted;
        }

        private bool TryReplaceByDiversityOrObjective(Heuristic heuristic, HeuristicResultPosition position)
        {
            int[] distancesToSolutionsInPool = new int[this.SolutionsInPool];
            float heuristicFinancialValue = heuristic.FinancialValue.GetHighestValueWithDefaulting(position);
            SortedDictionary<FiaCode, int[]> heuristicTreeSelection = heuristic.GetBestTrajectoryWithDefaulting(position).IndividualTreeSelectionBySpecies;
            int nearestLowerNeighborDistance = SolutionPool.UnknownDistance;
            int nearestLowerNeighborIndex = SolutionPool.UnknownNeighbor;
            for (int solutionIndex = 0; solutionIndex < this.SolutionsInPool; ++solutionIndex)
            {
                Heuristic eliteSolution = this.eliteSolutions[solutionIndex]!;
                float eliteFinancialValue = eliteSolution.FinancialValue.GetHighestValueWithDefaulting(position);
                if (eliteFinancialValue > heuristicFinancialValue)
                {
                    // for now, treat distances to solutions with more preferable objective functions as irrelevant
                    distancesToSolutionsInPool[solutionIndex] = SolutionPool.UnknownDistance;
                    continue;
                }

                // for now, use Hamming distance as it's interchangeable with Euclidean distance for binary decision variables
                // If needed, Euclidean distance can be used when multiple thinnings are allowed.
                SortedDictionary<FiaCode, int[]> eliteTreeSelection = eliteSolution.GetBestTrajectoryWithDefaulting(position).IndividualTreeSelectionBySpecies;
                int distanceToSolution = SolutionPool.GetHammingDistance(heuristicTreeSelection, eliteTreeSelection);
                if (distanceToSolution == 0)
                {
                    // solution is already in the pool
                    return false;
                }

                distancesToSolutionsInPool[solutionIndex] = distanceToSolution;
                if (distanceToSolution < nearestLowerNeighborDistance)
                {
                    nearestLowerNeighborDistance = distanceToSolution;
                    nearestLowerNeighborIndex = solutionIndex;
                }
            }

            // replace on contribution to diversity
            if ((nearestLowerNeighborIndex >= 0) && (nearestLowerNeighborDistance > this.MinimumNeighborDistance))
            {
                this.Replace(nearestLowerNeighborIndex, heuristic, position, distancesToSolutionsInPool, nearestLowerNeighborIndex);
                return true;
            }

            // replace worst if this is a new solution
            if ((heuristicFinancialValue > this.lowestEliteFinancialValue) && (nearestLowerNeighborDistance != 0))
            {
                this.Replace(this.lowestEliteIndex, heuristic, position, distancesToSolutionsInPool, nearestLowerNeighborIndex);
                return true;
            }

            return false;
        }
    }
}
