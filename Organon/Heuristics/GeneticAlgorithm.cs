﻿using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Osu.Cof.Ferm.Heuristics
{
    public class GeneticAlgorithm : Heuristic
    {
        public float EndStandardDeviation { get; set; }
        public int MaximumGenerations { get; set; }
        public float MutationProbability { get; set; }
        public int PopulationSize { get; set; }
        public float ReservedPopulationProportion { get; set; }

        public GeneticAlgorithm(OrganonStand stand, OrganonConfiguration organonConfiguration, int planningPeriods, Objective objective)
            : base(stand, organonConfiguration, planningPeriods, objective)
        {
            this.EndStandardDeviation = 0.01F;
            this.MaximumGenerations = 50;
            this.MutationProbability = 0.5F;
            this.PopulationSize = 30;
            this.ReservedPopulationProportion = 0.5F;

            this.ObjectiveFunctionByMove = new List<float>(this.MaximumGenerations);
        }

        public override string GetName()
        {
            return "Genetic";
        }

        private double GetMaximumFitnessAndVariance(GeneticPopulation generation)
        {
            float highestFitness = Single.MinValue;
            float sum = 0.0F;
            float sumOfSquares = 0.0F;
            for (int individualIndex = 0; individualIndex < generation.Size; ++individualIndex)
            {
                float individualFitness = generation.IndividualFitness[individualIndex];
                sum += individualFitness;
                sumOfSquares += individualFitness * individualFitness;
                if (individualFitness > highestFitness)
                {
                    highestFitness = individualFitness;
                }
            }

            // TODO: guarantee best individual in population is included in breeding
            // Debug.Assert(highestFitness >= this.BestObjectiveFunction);

            float n = (float)generation.Size;
            float meanHarvest = sum / n;
            float variance = sumOfSquares / n - meanHarvest * meanHarvest;
            return variance;
        }

        public override TimeSpan Run()
        {
            if (this.EndStandardDeviation <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(this.EndStandardDeviation));
            }
            if (this.MaximumGenerations < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(this.MaximumGenerations));
            }
            if (this.PopulationSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(this.PopulationSize));
            }
            if ((this.ReservedPopulationProportion < 0.0) || (this.ReservedPopulationProportion > 1.0))
            {
                throw new ArgumentOutOfRangeException(nameof(this.ReservedPopulationProportion));
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // begin with population of random harvest schedules
            // TODO: CopyTreeSelectionFrom() for initializing tree selection?
            // TODO: should incoming schedule on this.CurrentSolution be one of the individuals in the population?
            int initialTreeRecordCount = this.GetInitialTreeRecordCount();
            int treeSelectionCapacity = Constant.Simd128x4.Width * (initialTreeRecordCount / Constant.Simd128x4.Width + 1);
            GeneticPopulation currentGeneration = new GeneticPopulation(this.PopulationSize, this.CurrentTrajectory.HarvestPeriods, this.ReservedPopulationProportion, treeSelectionCapacity);
            currentGeneration.RandomizeSchedule(this.Objective.HarvestPeriodSelection);
            OrganonStandTrajectory individualTrajectory = new OrganonStandTrajectory(this.CurrentTrajectory);
            this.BestObjectiveFunction = Single.MinValue;
            for (int individualIndex = 0; individualIndex < this.PopulationSize; ++individualIndex)
            {
                int[] individualTreeSelection = currentGeneration.IndividualTreeSelections[individualIndex];
                for (int treeIndex = 0; treeIndex < individualTreeSelection.Length; ++treeIndex)
                {
                    individualTrajectory.SetTreeSelection(treeIndex, individualTreeSelection[treeIndex]);
                }
                individualTrajectory.Simulate();
                float individualFitness = this.GetObjectiveFunction(individualTrajectory);
                currentGeneration.IndividualFitness[individualIndex] = individualFitness;
                if (individualFitness > this.BestObjectiveFunction)
                {
                    this.BestObjectiveFunction = individualFitness;
                    this.BestTrajectory.Copy(individualTrajectory);
                }
                this.ObjectiveFunctionByMove.Add(this.BestObjectiveFunction);
            }

            // for each generation of size n, perform n fertile matings
            double endVariance = this.EndStandardDeviation * this.EndStandardDeviation;
            double treeScalingFactor = ((double)initialTreeRecordCount - Constant.RoundToZeroTolerance) / (double)UInt16.MaxValue;
            double mutationScalingFactor = 1.0 / (double)UInt16.MaxValue;
            double variance = this.GetMaximumFitnessAndVariance(currentGeneration);
            GeneticPopulation nextGeneration = new GeneticPopulation(currentGeneration);
            OrganonStandTrajectory firstChildTrajectory = individualTrajectory;
            OrganonStandTrajectory secondChildTrajectory = new OrganonStandTrajectory(this.CurrentTrajectory);
            for (int generationIndex = 1; (generationIndex < this.MaximumGenerations) && (variance > endVariance); ++generationIndex)
            {
                currentGeneration.RecalculateMatingDistributionFunction();
                for (int matingIndex = 0; matingIndex < currentGeneration.Size; ++matingIndex)
                {
                    // crossover parents' genetic material to create offsprings' genetic material
                    currentGeneration.FindParents(out int firstParentIndex, out int secondParentIndex);
                    int crossoverPosition = (int)(treeScalingFactor * this.GetTwoPseudorandomBytesAsFloat());
                    int[] firstParentHarvestSchedule = currentGeneration.IndividualTreeSelections[firstParentIndex];
                    int[] secondParentHarvestSchedule = currentGeneration.IndividualTreeSelections[secondParentIndex];
                    for (int treeIndex = 0; treeIndex < crossoverPosition; ++treeIndex)
                    {
                        firstChildTrajectory.SetTreeSelection(treeIndex, firstParentHarvestSchedule[treeIndex]);
                        secondChildTrajectory.SetTreeSelection(treeIndex, secondParentHarvestSchedule[treeIndex]);
                    }
                    for (int treeIndex = crossoverPosition; treeIndex < initialTreeRecordCount; ++treeIndex)
                    {
                        firstChildTrajectory.SetTreeSelection(treeIndex, secondParentHarvestSchedule[treeIndex]);
                        secondChildTrajectory.SetTreeSelection(treeIndex, firstParentHarvestSchedule[treeIndex]);
                    }

                    // maybe perform mutations
                    // TODO: investigate effect of mutations other than 2-opt exchange
                    double firstProbability = mutationScalingFactor * this.GetTwoPseudorandomBytesAsFloat();
                    if (firstProbability < this.MutationProbability)
                    {
                        int firstTreeIndex = (int)(treeScalingFactor * this.GetTwoPseudorandomBytesAsFloat());
                        int secondTreeIndex = (int)(treeScalingFactor * this.GetTwoPseudorandomBytesAsFloat());
                        int harvestPeriod = firstChildTrajectory.GetTreeSelection(firstTreeIndex);
                        firstChildTrajectory.SetTreeSelection(firstTreeIndex, firstChildTrajectory.GetTreeSelection(secondTreeIndex));
                        firstChildTrajectory.SetTreeSelection(secondTreeIndex, harvestPeriod);
                    }
                    double secondProbability = mutationScalingFactor * this.GetTwoPseudorandomBytesAsFloat();
                    if (secondProbability < this.MutationProbability)
                    {
                        int firstTreeIndex = (int)(treeScalingFactor * this.GetTwoPseudorandomBytesAsFloat());
                        int secondTreeIndex = (int)(treeScalingFactor * this.GetTwoPseudorandomBytesAsFloat());
                        int harvestPeriod = secondParentHarvestSchedule[firstTreeIndex];
                        secondChildTrajectory.SetTreeSelection(firstTreeIndex, firstChildTrajectory.GetTreeSelection(secondTreeIndex));
                        secondChildTrajectory.SetTreeSelection(secondTreeIndex, harvestPeriod);
                    }

                    // evaluate fitness of offspring
                    firstChildTrajectory.Simulate();
                    float firstChildFitness = this.GetObjectiveFunction(firstChildTrajectory);

                    secondChildTrajectory.Simulate();
                    float secondChildFitness = this.GetObjectiveFunction(secondChildTrajectory);

                    // identify the fittest individual among the two parents and the two offspring and place it in the next generation
                    float firstParentFitness = currentGeneration.IndividualFitness[firstParentIndex];
                    float secondParentFitness = currentGeneration.IndividualFitness[secondParentIndex];

                    bool firstChildFittest = firstChildFitness > secondChildFitness;
                    float fittestChildFitness = firstChildFittest ? firstChildFitness : secondChildFitness;
                    bool firstParentFittest = firstParentFitness > secondParentFitness;
                    float fittestParentFitness = firstParentFittest ? firstParentFitness : secondParentFitness;

                    if (fittestChildFitness > fittestParentFitness)
                    {
                        // fittest individual is a child
                        nextGeneration.IndividualFitness[matingIndex] = fittestChildFitness;
                        if (firstChildFittest)
                        {
                            firstChildTrajectory.CopyTreeSelectionTo(nextGeneration.IndividualTreeSelections[matingIndex]);
                            Array.Copy(firstChildTrajectory.HarvestVolumesByPeriod, 0, nextGeneration.HarvestVolumesByPeriod[matingIndex], 0, firstChildTrajectory.HarvestVolumesByPeriod.Length);
                            if (firstChildFitness > this.BestObjectiveFunction)
                            {
                                this.BestObjectiveFunction = firstChildFitness;
                                this.BestTrajectory.Copy(firstChildTrajectory);
                            }
                        }
                        else
                        {
                            secondChildTrajectory.CopyTreeSelectionTo(nextGeneration.IndividualTreeSelections[matingIndex]);
                            Array.Copy(secondChildTrajectory.HarvestVolumesByPeriod, 0, nextGeneration.HarvestVolumesByPeriod[matingIndex], 0, secondChildTrajectory.HarvestVolumesByPeriod.Length);
                            if (secondChildFitness > this.BestObjectiveFunction)
                            {
                                this.BestObjectiveFunction = secondChildFitness;
                                this.BestTrajectory.Copy(secondChildTrajectory);
                            }
                        }
                    }
                    else
                    {
                        // fittest individual is a parent
                        nextGeneration.IndividualFitness[matingIndex] = fittestParentFitness;
                        if (firstParentFittest)
                        {
                            nextGeneration.IndividualTreeSelections[matingIndex] = firstParentHarvestSchedule;
                            nextGeneration.HarvestVolumesByPeriod[matingIndex] = currentGeneration.HarvestVolumesByPeriod[firstParentIndex];
                        }
                        else
                        {
                            nextGeneration.IndividualTreeSelections[matingIndex] = secondParentHarvestSchedule;
                            nextGeneration.HarvestVolumesByPeriod[matingIndex] = currentGeneration.HarvestVolumesByPeriod[secondParentIndex];
                        }
                    }

                    this.ObjectiveFunctionByMove.Add(this.BestObjectiveFunction);
                }

                GeneticPopulation generationSwapPointer = currentGeneration;
                currentGeneration = nextGeneration;
                nextGeneration = generationSwapPointer;
                variance = this.GetMaximumFitnessAndVariance(currentGeneration);
            }

            this.CurrentTrajectory.Copy(this.BestTrajectory);

            stopwatch.Stop();
            return stopwatch.Elapsed;
        }
    }
}
