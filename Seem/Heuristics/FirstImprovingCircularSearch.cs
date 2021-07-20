using Osu.Cof.Ferm.Extensions;
using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Osu.Cof.Ferm.Heuristics
{
    public class FirstImprovingCircularSearch : SingleTreeHeuristic<HeuristicParameters>
    {
        public bool IsStochastic { get; set; }
        public int MaximumIterations { get; set; }

        public FirstImprovingCircularSearch(OrganonStand stand, HeuristicParameters heuristicParameters, RunParameters runParameters)
            : base(stand, heuristicParameters, runParameters)
        {
            this.IsStochastic = false;
            this.MaximumIterations = Constant.HeuristicDefault.FirstCircularIterationMultiplier * stand.GetTreeRecordCount();
        }

        private bool EvaluateMove(MoveDirection moveDirection, MoveState moveState, HeuristicResultPosition position)
        {
            int currentThinningPeriodIndex = moveState.UncompactedPeriodIndices[moveState.TreeIndex];
            int candidateThinningPeriodIndex = currentThinningPeriodIndex + (moveDirection == MoveDirection.Decrement ? -1 : 1);

            int candidateHarvestPeriod = moveState.ThinningPeriods[candidateThinningPeriodIndex];
            int currentHarvestPeriod = this.CurrentTrajectory.GetTreeSelection(moveState.TreeIndex); // capture for revert
            Debug.Assert(currentHarvestPeriod != candidateHarvestPeriod);
            moveState.CandidateTrajectory.SetTreeSelection(moveState.TreeIndex, candidateHarvestPeriod);
            moveState.PerfCounters.GrowthModelTimesteps += moveState.CandidateTrajectory.Simulate();

            float candidateFinancialValue = this.GetFinancialValue(moveState.CandidateTrajectory, position.FinancialIndex);
            bool acceptMove = candidateFinancialValue > moveState.AcceptedFinancialValue;
            if (acceptMove)
            {
                // accept change of no cut-cut decision if it improves upon the best solution
                this.CurrentTrajectory.CopyTreeGrowthFrom(moveState.CandidateTrajectory);
                moveState.AcceptedFinancialValue = candidateFinancialValue;
                moveState.MovesSinceLastImprovement = 0;
                moveState.UncompactedPeriodIndices[moveState.TreeIndex] = candidateThinningPeriodIndex;
                ++moveState.PerfCounters.MovesAccepted;
            }
            else
            {
                // otherwise, revert changes candidate trajectory for considering next tree's move
                moveState.CandidateTrajectory.SetTreeSelection(moveState.TreeIndex, currentHarvestPeriod);
                ++moveState.MovesSinceLastImprovement;
                ++moveState.PerfCounters.MovesRejected;
            }

            this.FinancialValue.AddMove(moveState.AcceptedFinancialValue, candidateFinancialValue);
            this.MoveLog.TreeIDByMove.Add(moveState.TreeIndex);

            return acceptMove;
        }

        public override string GetName()
        {
            if (this.IsStochastic)
            {
                return "FirstCircularStochastic";
            }
            return "FirstCircular";
        }

        public override HeuristicPerformanceCounters Run(HeuristicResultPosition position, HeuristicResults results)
        {
            if (this.MaximumIterations < 1)
            {
                throw new InvalidOperationException(nameof(this.MaximumIterations));
            }

            Stopwatch stopwatch = new();
            stopwatch.Start();

            int treesRandomizedInConstructingInitialTreeSelection = this.ConstructTreeSelection(position, results);
            MoveState moveState = new(this.CurrentTrajectory, treesRandomizedInConstructingInitialTreeSelection);
            moveState.AcceptedFinancialValue = this.EvaluateInitialSelection(position, this.MaximumIterations, moveState.PerfCounters);

            int lastImprovingSourceTreeIndex = 0;
            int sourceTreeIndex = moveState.UncompactedPeriodIndices.Length;
            for (int iteration = 0; iteration < this.MaximumIterations; ++iteration)
            {
                if (sourceTreeIndex >= moveState.UncompactedTreeIndices.Length)
                {
                    if (this.IsStochastic)
                    {
                        // re-randomize since either moves are just starting or a complete pass has been made through all trees
                        // TODO: investigate biased randomization
                        this.Pseudorandom.Shuffle(moveState.UncompactedTreeIndices, lastImprovingSourceTreeIndex);
                    }
                    sourceTreeIndex = 0;
                }

                // evaluate an alternate harvest time for this tree
                // The possibilities are
                //   1) The tree is harvested using the first period listed and can only be shifted to a later harvest index.
                //   2) The tree is harvested using the last period listed and can only be shifted to an earlier harvest index.
                //   3) There are two or more thins and the tree is currently assigned to a middle index, so can be shifted in both directions.
                // TODO: in the third case, evaluate strategies for guessing which direction to try first
                moveState.TreeIndex = moveState.UncompactedTreeIndices[sourceTreeIndex];
                int currentPeriodIndex = moveState.UncompactedPeriodIndices[moveState.TreeIndex];
                bool canDecrementPeriodIndex = currentPeriodIndex > 0;
                bool canIncrementPeriodIndex = currentPeriodIndex != moveState.ThinningPeriods.Count - 1;
                MoveDirection firstMoveDirection = canDecrementPeriodIndex ? MoveDirection.Decrement : MoveDirection.Increment;
                if (this.EvaluateMove(firstMoveDirection, moveState, position))
                {
                    lastImprovingSourceTreeIndex = sourceTreeIndex;
                }
                else if (canIncrementPeriodIndex)
                {
                    if (this.EvaluateMove(MoveDirection.Increment, moveState, position))
                    {
                        lastImprovingSourceTreeIndex = sourceTreeIndex;
                    }
                }

                if (moveState.MovesSinceLastImprovement >= moveState.UncompactedTreeIndices.Length)
                {
                    // all trees have been evaluated and no improving move was found
                    // Therefore, a first order local maxima has been found. For now, consider this convergence and terminate local search.
                    break;
                }
                ++sourceTreeIndex;
            }

            this.CopyTreeGrowthToBestTrajectory(this.CurrentTrajectory);

            stopwatch.Stop();
            moveState.PerfCounters.Duration = stopwatch.Elapsed;
            return moveState.PerfCounters;
        }

        private class MoveState
        {
            public float AcceptedFinancialValue { get; set; }
            public OrganonStandTrajectory CandidateTrajectory { get; private init; }
            public int MovesSinceLastImprovement { get; set; }
            public HeuristicPerformanceCounters PerfCounters { get; private init; }
            public IList<int> ThinningPeriods { get; private init; }
            public int TreeIndex { get; set; }
            public int[] UncompactedPeriodIndices { get; private init; }
            public int[] UncompactedTreeIndices { get; private init; }

            public MoveState(OrganonStandTrajectory trajectory, int treesRandomizedInConstruction)
            {
                int initialTreeRecordCount = trajectory.GetInitialTreeRecordCount();
                this.AcceptedFinancialValue = Single.MinValue;
                this.CandidateTrajectory = new(trajectory);
                this.MovesSinceLastImprovement = 0;
                this.PerfCounters = new();
                this.PerfCounters.TreesRandomizedInConstruction = treesRandomizedInConstruction;
                this.ThinningPeriods = trajectory.Treatments.GetHarvestPeriods();
                this.TreeIndex = 0;
                this.UncompactedPeriodIndices = trajectory.GetHarvestPeriodIndices(this.ThinningPeriods);
                this.UncompactedTreeIndices = ArrayExtensions.CreateSequentialIndices(initialTreeRecordCount);
            }
        }
    }
}
