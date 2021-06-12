﻿using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Osu.Cof.Ferm.Heuristics
{
    public class Population : SolutionPool
    {
        private readonly SortedDictionary<float, List<int>> individualIndexByFitness;
        private readonly float[] matingDistributionFunction;
        private float reservedPopulationProportion;

        public float[] IndividualFitness { get; private init; }
        // TODO: change to storage by species rather than flat array
        public int[][] IndividualTreeSelections { get; private init; }
        public int TreeCount { get; private set; }

        public Population(int capacity, float reservedPopulationProportion, int treeCount)
            : base(capacity)
        {
            this.individualIndexByFitness = new SortedDictionary<float, List<int>>();
            this.matingDistributionFunction = new float[capacity];
            this.reservedPopulationProportion = reservedPopulationProportion;

            this.IndividualFitness = new float[capacity];
            this.IndividualTreeSelections = new int[capacity][];
            this.SolutionsAccepted = 0;
            this.TreeCount = treeCount;

            int treeCapacity = Trees.GetCapacity(treeCount);
            for (int individualIndex = 0; individualIndex < capacity; ++individualIndex)
            {
                this.IndividualTreeSelections[individualIndex] = new int[treeCapacity];
            }
        }

        public Population(Population other)
            : this(other.PoolCapacity, other.reservedPopulationProportion, other.TreeCount)
        {
            this.CopyFrom(other);
        }

        public int ConstructTreeSelections(OrganonStandTrajectory standTrajectory, PopulationParameters parameters)
        {
            if (parameters.ConstructionGreediness != Constant.Grasp.FullyRandomConstructionForMaximization)
            {
                throw new NotSupportedException(nameof(parameters) + " partially greedy population initialization is not currently supported.");
            }
            if (parameters.InitialThinningProbability != Constant.HeuristicDefault.InitialThinningProbability)
            {
                throw new NotSupportedException(nameof(parameters) + ".InitialThinningProbability is not currently supported.");
            }

            Stand? standBeforeThinning = standTrajectory.StandByPeriod[0];
            if (standBeforeThinning == null)
            {
                throw new ArgumentOutOfRangeException(nameof(standTrajectory));
            }

            IList<int> thinningPeriods = standTrajectory.Treatments.GetValidThinningPeriods();
            if (thinningPeriods[0] != Constant.NoHarvestPeriod)
            {
                throw new NotSupportedException("First thinning selection is a harvest. Expected it to be the no harvest option.");
            }
            thinningPeriods.RemoveAt(0);

            // set up selection probabilities
            // With multiple diameter classes:
            //   Since each diameter class runs from a selection probability of zero to one the total probability range to traverse when intializing individuals
            //   is (1 - 0) * diameterClasses. Individuals in a population of size p are therefore spaced diameterClasses / p apart in probability. A loop
            //   initializing p individuals takes p - 1 steps, traversing a probability distance of (p - 1) / p * diameterClasses, and leaving a separation of
            //   diameterClasses / p between the last individual initialized and the individual the loop started on.
            // With a single diameter class;
            //   A special case of multiple diameter classes. Therefore, calculation remain the same.
            List<int> selectionProbabilityIndexByTree = parameters.InitializationMethod switch
            {
                PopulationInitializationMethod.DiameterClass => Population.GetTreeDiameterClasses(standBeforeThinning, parameters.InitializationClasses),
                PopulationInitializationMethod.DiameterQuantile => Population.GetTreeDiameterQuantiles(standBeforeThinning, parameters.InitializationClasses),
                PopulationInitializationMethod.HeightQuantile => Population.GetTreeHeightQuantiles(standBeforeThinning, parameters.InitializationClasses),
                _ => throw new NotSupportedException("Unhandled population initialization method " + parameters.InitializationMethod + ".")
            };
            List<float> selectionProbabilityByIndex = new(parameters.InitializationClasses);
            float selectionProbabilityIncrement = (float)parameters.InitializationClasses / this.PoolCapacity;
            float initialSelectionProbability = 0.5F * selectionProbabilityIncrement;
            for (int selectionClass = 0; selectionClass < parameters.InitializationClasses; ++selectionClass)
            {
                selectionProbabilityByIndex.Add(initialSelectionProbability);
            }

            // select trees and calculate distance
            // Tree selection code here is quite similar to Heuristic.ConstructTreeSelection().
            int treeSelectionsRandomized = 0;
            for (int individualIndex = 0; individualIndex < this.PoolCapacity; ++individualIndex)
            {
                // randomly select this individual's trees based on current diameter class selection probabilities
                int[] schedule = this.IndividualTreeSelections[individualIndex];
                Debug.Assert(selectionProbabilityIndexByTree.Count == schedule.Length);
                for (int treeIndex = 0; treeIndex < schedule.Length; ++treeIndex)
                {
                    int selectionIndex = selectionProbabilityIndexByTree[treeIndex];
                    if (selectionIndex >= 0) // diameter class of unused capacity is set to -1
                    {
                        int thinningPeriod = Constant.NoHarvestPeriod;
                        float probability = this.Pseudorandom.GetPseudorandomByteAsProbability();
                        float selectionProbability = selectionProbabilityByIndex[selectionIndex];
                        if (probability < selectionProbability)
                        {
                            // probability falls into the harvest fraction, choose equally among available harvest periods
                            float indexScalingFactor = (thinningPeriods.Count - Constant.RoundTowardsZeroTolerance) / selectionProbability;
                            int periodIndex = (int)(indexScalingFactor * probability);
                            thinningPeriod = thinningPeriods[periodIndex];
                            ++treeSelectionsRandomized;
                        }
                        schedule[treeIndex] = thinningPeriod;
                    }
                }

                // increment selection class probabilities
                for (int selectionClass = 0; selectionClass < parameters.InitializationClasses; ++selectionClass)
                {
                    float nextSelectionProbability = selectionProbabilityByIndex[selectionClass] + selectionProbabilityIncrement;
                    if (nextSelectionProbability < 1.0F)
                    {
                        // no rollover to next diameter class, so stop incrementing
                        selectionProbabilityByIndex[selectionClass] = nextSelectionProbability;
                        break;
                    }

                    // roll over this diameter class and continue on to increment the next largest diameter class
                    // Use roll over, rather than reset, to avoid creation of individuals with duplicate diameter class selection probabilities.
                    selectionProbabilityByIndex[selectionClass] = nextSelectionProbability - 1.0F;
                }

                // calculate distances to the tree selections which have already been added and set nearest neighbor of the individual
                // just generated
                this.UpdateNearestNeighborDistances(individualIndex);
            }

            return treeSelectionsRandomized;
        }

        public void CopyFrom(Population other)
        {
            Debug.Assert(Object.ReferenceEquals(this, other) == false);
            if (this.PoolCapacity != other.PoolCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(other));
            }

            // base fields
            Array.Copy(other.DistanceMatrix, 0, this.DistanceMatrix, 0, this.DistanceMatrix.Length);
            this.MinimumNeighborDistance = other.MinimumNeighborDistance;
            this.MinimumNeighborIndex = other.MinimumNeighborIndex;
            Array.Copy(other.NearestNeighborIndex, 0, this.NearestNeighborIndex, 0, this.NearestNeighborIndex.Length);

            // self fields
            this.individualIndexByFitness.Clear();
            foreach (KeyValuePair<float, List<int>> individualOfFitness in other.individualIndexByFitness)
            {
                this.individualIndexByFitness.Add(individualOfFitness.Key, new List<int>(individualOfFitness.Value));
            }

            Array.Copy(other.IndividualFitness, 0, this.IndividualFitness, 0, this.IndividualFitness.Length);
            for (int individualIndex = 0; individualIndex < other.PoolCapacity; ++individualIndex)
            {
                other.IndividualTreeSelections[individualIndex].CopyToExact(this.IndividualTreeSelections[individualIndex]);
            }

            Array.Copy(other.matingDistributionFunction, 0, this.matingDistributionFunction, 0, this.matingDistributionFunction.Length);
            this.SolutionsAccepted = other.SolutionsAccepted;
            this.reservedPopulationProportion = other.reservedPopulationProportion;
            this.TreeCount = other.TreeCount;
        }

        public void CrossoverKPoint(int points, int firstParentIndex, int secondParentIndex, int[] sortOrder, StandTrajectory firstChildTrajectory, StandTrajectory secondChildTrajectory)
            {
                // get parents' schedules
                int[] firstParentHarvestSchedule = this.IndividualTreeSelections[firstParentIndex];
                int[] secondParentHarvestSchedule = this.IndividualTreeSelections[secondParentIndex];
                int treeRecordCount = sortOrder.Length; // sortOrder is of tree record count, parent schedules are of capacity length
                Debug.Assert(firstParentHarvestSchedule.Length == secondParentHarvestSchedule.Length);

                // find length and position of crossover
                float treeScalingFactor = (treeRecordCount - Constant.RoundTowardsZeroTolerance) / UInt16.MaxValue;
                int[] crossoverPoints = new int[points];
                for (int pointIndex = 0; pointIndex < points; ++pointIndex)
                {
                    crossoverPoints[pointIndex] = (int)(treeScalingFactor * this.Pseudorandom.GetTwoPseudorandomBytesAsFloat());
                }
                Array.Sort(crossoverPoints);

                bool isCrossed = false; // initial value is unimportant as designation of first vs second child is arbitrary
                int crossoverIndex = 0;
                int crossoverPosition = crossoverPoints[crossoverIndex];
                for (int sortIndex = 0; sortIndex < treeRecordCount; ++sortIndex)
                {
                    if (crossoverPosition == sortIndex)
                    {
                        isCrossed = !isCrossed;
                        crossoverPosition = crossoverPoints[crossoverIndex];
                        ++crossoverIndex;
                    }

                    int treeIndex = sortOrder[sortIndex];
                    if (isCrossed)
                    {
                        firstChildTrajectory.SetTreeSelection(treeIndex, secondParentHarvestSchedule[treeIndex]);
                        secondChildTrajectory.SetTreeSelection(treeIndex, firstParentHarvestSchedule[treeIndex]);
                    }
                    else
                    {
                        firstChildTrajectory.SetTreeSelection(treeIndex, firstParentHarvestSchedule[treeIndex]);
                        secondChildTrajectory.SetTreeSelection(treeIndex, secondParentHarvestSchedule[treeIndex]);
                    }
                }
            }

        public void CrossoverUniform(int firstParentIndex, int secondParentIndex, float changeProbability, StandTrajectory firstChildTrajectory, StandTrajectory secondChildTrajectory)
        {
            // get parents' schedules
            int[] firstParentHarvestSchedule = this.IndividualTreeSelections[firstParentIndex];
            int[] secondParentHarvestSchedule = this.IndividualTreeSelections[secondParentIndex];
            Debug.Assert(firstParentHarvestSchedule.Length == secondParentHarvestSchedule.Length);

            for (int treeIndex = 0; treeIndex < firstParentHarvestSchedule.Length; ++treeIndex)
            {
                int firstHarvestPeriod = firstParentHarvestSchedule[treeIndex];
                int secondHarvestPeriod = secondParentHarvestSchedule[treeIndex];
                if (firstHarvestPeriod != secondHarvestPeriod)
                {
                    int harvestPeriodBuffer = firstHarvestPeriod;
                    if (this.Pseudorandom.GetPseudorandomByteAsProbability() < changeProbability)
                    {
                        firstHarvestPeriod = secondHarvestPeriod;
                    }
                    if (this.Pseudorandom.GetPseudorandomByteAsProbability() < changeProbability)
                    {
                        secondHarvestPeriod = harvestPeriodBuffer;
                    }
                }
                firstChildTrajectory.SetTreeSelection(treeIndex, firstHarvestPeriod);
                secondChildTrajectory.SetTreeSelection(treeIndex, secondHarvestPeriod);
            }
        }

        public void FindParents(out int firstParentIndex, out int secondParentIndex)
        {
            // find first parent
            // TODO: check significance of quantization effects from use of two random bytes
            float probabilityScaling = 1.0F / (float)UInt16.MaxValue;
            float firstParentCumlativeProbability = probabilityScaling * this.Pseudorandom.GetTwoPseudorandomBytesAsFloat();
            firstParentIndex = this.PoolCapacity - 1; // numerical precision may result in the last CDF value being slightly below 1
            for (int individualIndex = 0; individualIndex < this.PoolCapacity; ++individualIndex)
            {
                if (firstParentCumlativeProbability <= this.matingDistributionFunction[individualIndex])
                {
                    firstParentIndex = individualIndex;
                    break;
                }
            }

            // find second parent
            // TODO: check significance of allowing selfing
            // TOOD: investigate selection pressure effect of choosing second parent randomly
            float secondParentCumlativeProbability = probabilityScaling * this.Pseudorandom.GetTwoPseudorandomBytesAsFloat();
            secondParentIndex = this.PoolCapacity - 1;
            for (int individualIndex = 0; individualIndex < this.PoolCapacity; ++individualIndex)
            {
                if (secondParentCumlativeProbability <= this.matingDistributionFunction[individualIndex])
                {
                    secondParentIndex = individualIndex;
                    break;
                }
            }
        }

        private static List<int> GetTreeDiameterClasses(Stand standBeforeThinning, int diameterClasses)
        {
            if (diameterClasses < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(diameterClasses));
            }

            float maximumDbh = Single.MinValue;
            float minimumDbh = Single.MaxValue;
            int totalCapacity = 0;
            foreach (Trees treesOfSpecies in standBeforeThinning.TreesBySpecies.Values)
            {
                for (int treeIndex = 0; treeIndex < treesOfSpecies.Count; ++treeIndex)
                {
                    maximumDbh = MathF.Max(maximumDbh, treesOfSpecies.Dbh[treeIndex]);
                    minimumDbh = MathF.Min(maximumDbh, treesOfSpecies.Dbh[treeIndex]);
                }
                totalCapacity += treesOfSpecies.Capacity;
            }

            List<int> diameterClassByTree = new(totalCapacity);
            float diameterClassWidth = (maximumDbh - minimumDbh) / diameterClasses;
            foreach (Trees treesOfSpecies in standBeforeThinning.TreesBySpecies.Values)
            {
                for (int treeIndex = 0; treeIndex < treesOfSpecies.Count; ++treeIndex)
                {
                    int diameterClass = (int)((treesOfSpecies.Dbh[treeIndex] - minimumDbh) / diameterClassWidth);
                    if (diameterClass < 0)
                    {
                        diameterClass = 0; // limit numerical error
                    }
                    else if (diameterClass >= diameterClasses)
                    {
                        diameterClass = diameterClasses - 1; // limit numerical error
                    }
                    diameterClassByTree.Add(diameterClass);
                }
                for (int treeIndex = treesOfSpecies.Count; treeIndex < treesOfSpecies.Capacity; ++treeIndex)
                {
                    diameterClassByTree.Add(-1);
                }
            }

            return diameterClassByTree;
        }

        private static List<int> GetTreeDiameterQuantiles(Stand standBeforeThinning, int diameterQuantiles)
        {
            if (standBeforeThinning.TreesBySpecies.Count != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(standBeforeThinning));
            }

            Trees treesOfSpecies = standBeforeThinning.TreesBySpecies.Values.First();
            return Population.GetTreeQuantiles(treesOfSpecies, treesOfSpecies.GetDbhSortOrder, diameterQuantiles);
        }

        private static List<int> GetTreeHeightQuantiles(Stand standBeforeThinning, int diameterQuantiles)
        {
            if (standBeforeThinning.TreesBySpecies.Count != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(standBeforeThinning));
            }

            Trees treesOfSpecies = standBeforeThinning.TreesBySpecies.Values.First();
            return Population.GetTreeQuantiles(treesOfSpecies, treesOfSpecies.GetHeightSortOrder, diameterQuantiles);
        }

        private static List<int> GetTreeQuantiles(Trees treesOfSpecies, Func<int[]> getSortOrder, int quantiles)
        {
            if (quantiles < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(quantiles));
            }

            int[] treeSortOrder = getSortOrder.Invoke();
            List<int> quantileByTree = new(treesOfSpecies.Capacity);
            float quantileScalingFactor = (quantiles - Constant.RoundTowardsZeroTolerance) / treesOfSpecies.Count;
            for (int treeIndex = 0; treeIndex < treesOfSpecies.Count; ++treeIndex)
            {
                int quantile = (int)MathF.Floor(quantileScalingFactor * treeSortOrder[treeIndex]);
                Debug.Assert(quantile < quantiles);
                quantileByTree.Add(quantile);
            }
            for (int treeIndex = treesOfSpecies.Count; treeIndex < treesOfSpecies.Capacity; ++treeIndex)
            {
                quantileByTree.Add(-1);
            }
            return quantileByTree;
        }

        public void InsertFitness(int individualIndex, float newFitness)
        {
            this.IndividualFitness[individualIndex] = newFitness;
            if (this.individualIndexByFitness.TryGetValue(newFitness, out List<int>? probablyClones))
            {
                // initial population randomization happened to create two (or more) individuals with identical fitness
                // This is unlikely in large problems, but may not be uncommon in small test problems.
                probablyClones.Add(individualIndex);
            }
            else
            {
                this.individualIndexByFitness.Add(newFitness, new List<int>() { individualIndex });
            }

            ++this.SolutionsAccepted;
        }

        public void RecalculateMatingDistributionFunction()
        {
            // find cumulative distribution function (CDF) representing prevalence of individuals in population
            // The reserved proportion is allocated equally across all individuals and guarantees a minimum presence of low fitness individuals.
            float totalFitness = 0.0F;
            for (int individualIndex = 0; individualIndex < this.PoolCapacity; ++individualIndex)
            {
                // for now, clamp negative objective functions to small positive ones to avoid total fitness of zero and NaNs
                float individualFitness = Math.Max(this.IndividualFitness[individualIndex], 0.0001F);
                totalFitness += individualFitness;
            }

            float guaranteedProportion = this.reservedPopulationProportion / this.PoolCapacity;
            float fitnessProportion = 1.0F - this.reservedPopulationProportion;
            this.matingDistributionFunction[0] = guaranteedProportion + fitnessProportion * this.IndividualFitness[0] / totalFitness;
            for (int individualIndex = 1; individualIndex < this.PoolCapacity; ++individualIndex)
            {
                float individualFitness = Math.Max(this.IndividualFitness[individualIndex], 0.0F);
                this.matingDistributionFunction[individualIndex] = matingDistributionFunction[individualIndex - 1];
                this.matingDistributionFunction[individualIndex] += guaranteedProportion + fitnessProportion * individualFitness / totalFitness;

                Debug.Assert(this.matingDistributionFunction[individualIndex] > this.matingDistributionFunction[individualIndex - 1]);
                Debug.Assert(this.matingDistributionFunction[individualIndex] <= 1.00001);
            }
        }

        private void Replace(float currentFitness, List<int> currentIndices, float newFitness, StandTrajectory trajectory, int[] neigborDistances, int nearestLowerNeighborIndex)
        {
            if ((nearestLowerNeighborIndex != SolutionPool.UnknownNeighbor) && (neigborDistances[nearestLowerNeighborIndex] == 0))
            {
                throw new ArgumentOutOfRangeException(nameof(neigborDistances), "Attempt to introduce a duplicate individual.");
            }

            Debug.Assert(currentIndices.Count > 0);
            int replacementIndex;
            if (currentIndices.Count == 1)
            {
                // reassign individual index to new fitness
                this.individualIndexByFitness.Remove(currentFitness);
                replacementIndex = currentIndices[0];

                if (this.individualIndexByFitness.TryGetValue(newFitness, out List<int>? existingIndices))
                {
                    // prepend individual to list to maximize population change since removal below operates from end of list
                    existingIndices.Insert(0, replacementIndex);
                }
                else
                {
                    // OK to reuse existing list due to ownership release
                    this.individualIndexByFitness.Add(newFitness, currentIndices);
                }
            }
            else
            {
                // remove presumed clone from existing list
                replacementIndex = currentIndices[^1];
                if (currentFitness != newFitness)
                {
                    currentIndices.RemoveAt(currentIndices.Count - 1);
                    if (this.individualIndexByFitness.TryGetValue(newFitness, out List<int>? existingList))
                    {
                        // could potentially no-op by undoing the previous RemoveAt(), but this is likely a rare case
                        existingList.Add(replacementIndex);
                    }
                    else
                    {
                        // mainline case
                        this.individualIndexByFitness.Add(newFitness, new List<int>() { replacementIndex });
                    }
                }
            }

            this.IndividualFitness[replacementIndex] = newFitness;
            trajectory.CopyTreeSelectionTo(this.IndividualTreeSelections[replacementIndex]);

            this.UpdateNearestNeighborDistances(replacementIndex, neigborDistances, nearestLowerNeighborIndex);
            ++this.SolutionsAccepted;
        }

        public bool TryReplace(float newFitness, StandTrajectory trajectory, PopulationReplacementStrategy replacementStrategy)
        {
            return replacementStrategy switch
            {
                PopulationReplacementStrategy.ContributionOfDiversityReplaceWorst => this.TryReplaceByDiversityOrFitness(newFitness, trajectory),
                PopulationReplacementStrategy.ReplaceWorst => this.TryReplaceWorst(newFitness, trajectory, new int[] { SolutionPool.UnknownDistance }, SolutionPool.UnknownNeighbor),
                _ => throw new NotSupportedException(String.Format("Unhandled replacement strategy {0}.", replacementStrategy))
            };
        }

        private bool TryReplaceByDiversityOrFitness(float newFitness, StandTrajectory trajectory)
        {
            // find nearest neighbor with a lower objective function value
            int[] candidateSelection = new int[Trees.GetCapacity(this.TreeCount)];
            trajectory.CopyTreeSelectionTo(candidateSelection);

            int[] distancesToIndividuals = new int[this.individualIndexByFitness.Count];
            Array.Fill(distancesToIndividuals, SolutionPool.UnknownDistance);
            int nearestLowerNeighborDistance = Int32.MaxValue;
            int nearestLowerNeighborIndex = -1;
            KeyValuePair<float, List<int>> nearestNeighbor = default;
            foreach (KeyValuePair<float, List<int>> fitnessSortedIndividual in this.individualIndexByFitness)
            {
                float individualFitness = fitnessSortedIndividual.Key;
                if (individualFitness > newFitness)
                {
                    // under current replacement logic, distances to solutions with more preferable objective functions are irrelevant
                    // and therefore don't need to be evaluated
                    break;
                }
                for (int solutionIndex = 0; solutionIndex < fitnessSortedIndividual.Value.Count; ++solutionIndex)
                {
                    int individualIndex = fitnessSortedIndividual.Value[solutionIndex];

                    // for now, use Hamming distance as it's interchangeable with Euclidean distance for binary decision variables
                    // If needed, Euclidean distance or stand entry distance can be used when multiple thinnings are allowed.
                    int distanceToSolution = SolutionPool.GetHammingDistance(candidateSelection, this.IndividualTreeSelections[individualIndex]);
                    if (distanceToSolution == 0)
                    {
                        // this tree selection is already present in the population
                        return false;
                    }

                    distancesToIndividuals[solutionIndex] = Math.Min(distanceToSolution, distancesToIndividuals[solutionIndex]);
                    if (distanceToSolution < nearestLowerNeighborDistance)
                    {
                        nearestNeighbor = fitnessSortedIndividual;
                        nearestLowerNeighborDistance = distanceToSolution;
                        nearestLowerNeighborIndex = individualIndex;
                    }
                }
            }

            // since minimum value of the minimum neighbor distance is zero acceptance of duplicate solutions is blocked
            // since default value of the minimum neighbor distance is Int32.Max acceptance of solutions without a lower neighbor is blocked
            if ((nearestLowerNeighborIndex >= 0) && (nearestLowerNeighborDistance > this.MinimumNeighborDistance))
            {
                // the check against minimum neighbor distance prevents introduction of more than a pair of clones through this path
                // The remove worst path could potentially create higher numbers of clones, though this is unlikely.
                this.Replace(nearestNeighbor.Key, nearestNeighbor.Value, newFitness, trajectory, distancesToIndividuals, nearestLowerNeighborIndex);
                return true;
            }

            return this.TryReplaceWorst(newFitness, trajectory, distancesToIndividuals, nearestLowerNeighborIndex);
        }

        private bool TryReplaceWorst(float newFitness, StandTrajectory trajectory, int[] neighborDistances, int nearestLowerNeighborIndex)
        {
            float minimumFitness = this.individualIndexByFitness.Keys.First();
            if ((minimumFitness > newFitness) || 
                ((nearestLowerNeighborIndex != SolutionPool.UnknownNeighbor) && (neighborDistances[nearestLowerNeighborIndex] == 0)))
            {
                // no solution to replace if 1) new trajectory is not improving or 2) this solution is already known
                ++this.SolutionsRejected;
                return false;
            }

            List<int> replacementIndices = this.individualIndexByFitness[minimumFitness];
            this.Replace(minimumFitness, replacementIndices, newFitness, trajectory, neighborDistances, nearestLowerNeighborIndex);
            return true;
        }

        // factored out of ConstructTreeSelections() as a low level test hook
        internal void UpdateNearestNeighborDistances(int individualIndex)
        {
            int nearestNeighborDistance = Int32.MaxValue;
            int nearestNeighborIndex = -1;
            for (int neighborIndex = individualIndex + 1; neighborIndex < this.PoolCapacity; ++neighborIndex)
            {
                int neighborDistance = SolutionPool.GetHammingDistance(this.IndividualTreeSelections[individualIndex], this.IndividualTreeSelections[neighborIndex]);
                this.DistanceMatrix[individualIndex, neighborIndex] = neighborDistance;
                this.DistanceMatrix[neighborIndex, individualIndex] = neighborDistance;

                if (neighborDistance < nearestNeighborDistance)
                {
                    nearestNeighborDistance = neighborDistance;
                    nearestNeighborIndex = neighborIndex;
                }
            }

            this.NearestNeighborIndex[individualIndex] = nearestNeighborIndex;

            // check for update to minimum distance among all individuals in the population
            if (nearestNeighborDistance < this.MinimumNeighborDistance)
            {
                this.MinimumNeighborDistance = nearestNeighborDistance;
                this.MinimumNeighborIndex = nearestNeighborIndex;
            }

            // needed for testing, this.SolutionsInPool = this.PoolCapacity; in ConstructTreeSelections() would be equivalent
            ++this.SolutionsInPool;
        }
    }
}
