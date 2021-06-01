﻿using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Osu.Cof.Ferm.Heuristics
{
    public class ThresholdAccepting : SingleTreeHeuristic<HeuristicParameters>
    {
        public List<int> IterationsPerThreshold { get; private init; }
        public List<float> Thresholds { get; private init; }

        public ThresholdAccepting(OrganonStand stand, HeuristicParameters heuristicParameters, RunParameters runParameters)
            : base(stand, heuristicParameters, runParameters)
        {
            int treeRecords = stand.GetTreeRecordCount();
            this.IterationsPerThreshold = new List<int>() { (int)(11.5F * treeRecords), 25, (int)(7.5F * treeRecords) };
            this.Thresholds = new List<float>() { 1.0F, 0.999F, 1.0F };
        }

        public override string GetName()
        {
            return "ThresholdAccepting";
        }

        // similar to SimulatedAnnealing.Run(), differences are in move acceptance
        public override HeuristicPerformanceCounters Run(HeuristicSolutionPosition position, HeuristicSolutionIndex solutionIndex)
        {
            if (this.IterationsPerThreshold.Count < 1)
            {
                throw new InvalidOperationException(nameof(this.IterationsPerThreshold));
            }
            if (this.Thresholds.Count != this.IterationsPerThreshold.Count)
            {
                throw new InvalidOperationException(nameof(this.Thresholds));
            }
            foreach (float threshold in this.Thresholds)
            {
                if ((threshold < 0.0F) || (threshold > 1.0F))
                {
                    throw new InvalidOperationException(nameof(this.Thresholds));
                }
            }

            IList<int> thinningPeriods = this.CurrentTrajectory.Treatments.GetValidThinningPeriods();
            if ((thinningPeriods.Count < 2) || (thinningPeriods.Count > 3))
            {
                throw new NotSupportedException("Currently, only one or two thins are supported.");
            }

            Stopwatch stopwatch = new();
            stopwatch.Start();
            HeuristicPerformanceCounters perfCounters = new();

            perfCounters.TreesRandomizedInConstruction += this.ConstructTreeSelection(position, solutionIndex);
            this.EvaluateInitialSelection(this.IterationsPerThreshold.Sum(), perfCounters);

            float acceptedObjectiveFunction = this.BestObjectiveFunction;
            float treeIndexScalingFactor = (this.CurrentTrajectory.GetInitialTreeRecordCount() - Constant.RoundTowardsZeroTolerance) / UInt16.MaxValue;

            OrganonStandTrajectory candidateTrajectory = new(this.CurrentTrajectory);
            for (int thresholdIndex = 0; thresholdIndex < this.Thresholds.Count; ++thresholdIndex)
            {
                float iterations = this.IterationsPerThreshold[thresholdIndex];
                float threshold = this.Thresholds[thresholdIndex];
                for (int iterationInThreshold = 0; iterationInThreshold < iterations; ++iterationInThreshold)
                {
                    // if needed, support two opt moves
                    int treeIndex = (int)(treeIndexScalingFactor * this.Pseudorandom.GetTwoPseudorandomBytesAsFloat());
                    int currentHarvestPeriod = this.CurrentTrajectory.GetTreeSelection(treeIndex);
                    int candidateHarvestPeriod = this.GetOneOptCandidateRandom(currentHarvestPeriod, thinningPeriods);
                    Debug.Assert(candidateHarvestPeriod >= 0);

                    candidateTrajectory.SetTreeSelection(treeIndex, candidateHarvestPeriod);
                    perfCounters.GrowthModelTimesteps += candidateTrajectory.Simulate();

                    float candidateObjectiveFunction = this.GetObjectiveFunction(candidateTrajectory);
                    bool acceptMove = candidateObjectiveFunction > threshold * acceptedObjectiveFunction;
                    if (acceptMove)
                    {
                        acceptedObjectiveFunction = candidateObjectiveFunction; 
                        this.CurrentTrajectory.CopyTreeGrowthFrom(candidateTrajectory);
                        ++perfCounters.MovesAccepted;

                        if (acceptedObjectiveFunction > this.BestObjectiveFunction)
                        {
                            this.BestObjectiveFunction = acceptedObjectiveFunction;
                            this.BestTrajectory.CopyTreeGrowthFrom(this.CurrentTrajectory);
                        }
                    }
                    else
                    {
                        candidateTrajectory.SetTreeSelection(treeIndex, currentHarvestPeriod);
                        ++perfCounters.MovesRejected;
                    }

                    this.AcceptedObjectiveFunctionByMove.Add(acceptedObjectiveFunction);
                    this.CandidateObjectiveFunctionByMove.Add(candidateObjectiveFunction);
                    this.MoveLog.TreeIDByMove.Add(treeIndex);
                }
            }

            stopwatch.Stop();
            perfCounters.Duration = stopwatch.Elapsed;
            return perfCounters;
        }
    }
}
