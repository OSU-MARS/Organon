using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Osu.Cof.Ferm.Heuristics
{
    public abstract class PrescriptionHeuristic : Heuristic<PrescriptionParameters>
    {
        protected readonly PrescriptionAllMoveLog? allMoveLog;
        protected readonly PrescriptionFirstInFirstOutMoveLog? highestFinancialValueMoveLog;

        protected PrescriptionHeuristic(OrganonStand stand, PrescriptionParameters heuristicParameters, RunParameters runParameters, bool evaluatesAcrossRotationsAndDiscountRates)
            : base(stand, heuristicParameters, runParameters, evaluatesAcrossRotationsAndDiscountRates)
        {
            if (this.HeuristicParameters.LogAllMoves)
            {
                this.allMoveLog = new PrescriptionAllMoveLog();
            }
            else
            {
                // by default, store prescription intensities for only the highest LEV combination of thinning intensities found
                // This substantially reduces memory footprint in runs where many prescriptions are enumerated and helps to reduce the
                // size of objective log files. If needed, this can be changed to storing a larger number of prescriptions.
                this.highestFinancialValueMoveLog = new PrescriptionFirstInFirstOutMoveLog(runParameters.RotationLengths.Count, runParameters.Financial.Count, Constant.DefaultSolutionPoolSize);
            }
        }

        protected abstract void EvaluateThinningPrescriptions(HeuristicResultPosition position, HeuristicResults results, HeuristicPerformanceCounters perfCounters);

        public override IHeuristicMoveLog? GetMoveLog()
        {
            if (this.HeuristicParameters.LogAllMoves)
            {
                return this.allMoveLog;
            }
            else
            {
                return this.highestFinancialValueMoveLog;
            }
        }

        public override HeuristicPerformanceCounters Run(HeuristicResultPosition position, HeuristicResults results)
        {
            if (this.HeuristicParameters.MinimumConstructionGreediness != Constant.Grasp.FullyGreedyConstructionForMaximization)
            {
                throw new InvalidOperationException(nameof(this.HeuristicParameters.MinimumConstructionGreediness));
            }
            if (this.HeuristicParameters.InitialThinningProbability != 0.0F)
            {
                throw new InvalidOperationException(nameof(this.HeuristicParameters.InitialThinningProbability));
            }

            if ((this.HeuristicParameters.FromAbovePercentageUpperLimit < 0.0F) || (this.HeuristicParameters.FromAbovePercentageUpperLimit > 100.0F))
            {
                throw new InvalidOperationException(nameof(this.HeuristicParameters.FromAbovePercentageUpperLimit));
            }
            if ((this.HeuristicParameters.FromBelowPercentageUpperLimit < 0.0F) || (this.HeuristicParameters.FromBelowPercentageUpperLimit > 100.0F))
            {
                throw new InvalidOperationException(nameof(this.HeuristicParameters.FromBelowPercentageUpperLimit));
            }
            if ((this.HeuristicParameters.ProportionalPercentageUpperLimit < 0.0F) || (this.HeuristicParameters.ProportionalPercentageUpperLimit > 100.0F))
            {
                throw new InvalidOperationException(nameof(this.HeuristicParameters.ProportionalPercentageUpperLimit));
            }

            float intensityUpperBound = this.HeuristicParameters.Units switch
            {
                PrescriptionUnits.BasalAreaPerAcreRetained => 1000.0F,
                PrescriptionUnits.StemPercentageRemoved => 100.0F,
                _ => throw new NotSupportedException(String.Format("Unhandled units {0}.", this.HeuristicParameters.Units))
            };
            if ((this.HeuristicParameters.DefaultIntensityStepSize < this.HeuristicParameters.MinimumIntensityStepSize) ||
                (this.HeuristicParameters.DefaultIntensityStepSize > this.HeuristicParameters.MaximumIntensityStepSize))
            {
                throw new InvalidOperationException(nameof(this.HeuristicParameters.DefaultIntensityStepSize));
            }
            if ((this.HeuristicParameters.MaximumIntensity < this.HeuristicParameters.MinimumIntensity) || (this.HeuristicParameters.MaximumIntensity > intensityUpperBound))
            {
                throw new InvalidOperationException(nameof(this.HeuristicParameters.MaximumIntensity));
            }
            if ((this.HeuristicParameters.MaximumIntensityStepSize > intensityUpperBound) || (this.HeuristicParameters.MaximumIntensityStepSize < this.HeuristicParameters.MinimumIntensityStepSize))
            {
                throw new InvalidOperationException(nameof(this.HeuristicParameters.MaximumIntensityStepSize));
            }
            if (this.HeuristicParameters.MinimumIntensity < 0.0F)
            {
                throw new InvalidOperationException(nameof(this.HeuristicParameters.MinimumIntensity));
            }
            if (this.HeuristicParameters.MinimumIntensityStepSize < 0.0F)
            {
                throw new InvalidOperationException(nameof(this.HeuristicParameters.MinimumIntensityStepSize));
            }

            if (this.CurrentTrajectory.Treatments.Harvests.Count > 3)
            {
                throw new NotSupportedException("Enumeration of more than three thinnings is not currently supported.");
            }

            Stopwatch stopwatch = new();
            stopwatch.Start();
            HeuristicPerformanceCounters perfCounters = new();

            // call solution construction to reuse cached growth model timesteps and thinning prescriptions where practical
            // Prescriptions and tree selection are overwritten in prescription enumeration, leaving only the minor benefit of reusing a few
            // timesteps. However, since construction is fully greedy, new coordinate descents can begin from the copied solution without
            // modifying it.
            HeuristicResultPosition constructionSourcePosition = position;
            if (position.RotationIndex == Constant.AllRotationPosition)
            {
                // for now, choose a initial position to search from with all rotations and all financial scenarios here
                // This can be moved into lower level code if needed.
                constructionSourcePosition = new(position)
                {
                    FinancialIndex = Constant.HeuristicDefault.FinancialIndex, // for now, assume searching from default index is most efficient
                    RotationIndex = Constant.HeuristicDefault.RotationIndex
                };
            }
            perfCounters.TreesRandomizedInConstruction += this.ConstructTreeSelection(constructionSourcePosition, results);

            this.EvaluateThinningPrescriptions(position, results, perfCounters);

            stopwatch.Stop();
            perfCounters.Duration = stopwatch.Elapsed;
            if (this.highestFinancialValueMoveLog != null)
            {
                // since this.singleMoveLog.Add() is called only on improving moves it has no way of setting its count
                this.highestFinancialValueMoveLog.LengthInMoves = perfCounters.MovesAccepted + perfCounters.MovesRejected;
            }
            return perfCounters;
        }
    }
}
