using Osu.Cof.Ferm.Extensions;
using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Osu.Cof.Ferm.Heuristics
{
    public class PrescriptionCoordinateAscent: PrescriptionHeuristic
    {
        public bool Gradient { get; set; }
        public bool IsStochastic { get; set; }
        public bool RestartOnLocalMaximum { get; set; }

        public PrescriptionCoordinateAscent(OrganonStand stand, PrescriptionParameters heuristicParameters, RunParameters runParameters)
            : base(stand, heuristicParameters, runParameters, evaluatesAcrossRotationsAndDiscountRates: false)
        {
            this.Gradient = false;
            this.IsStochastic = false;
            this.RestartOnLocalMaximum = false;
        }

        private float EvaluateCandidatePosition(MoveState moveState, HeuristicPerformanceCounters perfCounters)
        {
            // skip moves which return to recently visited positions
            if (moveState.CandidatePositionIsRecentlyVisited())
            {
                return 0.0F;
            }

            // evaluate objective function at position since it has not been recently visited
            IList<IHarvest> harvests = this.CurrentTrajectory.Treatments.Harvests;
            Debug.Assert((harvests.Count < 4) && (moveState.CandidateIntensities.Length == 3 * harvests.Count));

            ThinByPrescription? firstThinPrescription = null;
            if (harvests.Count > 0)
            {
                firstThinPrescription = (ThinByPrescription)harvests[0];
                firstThinPrescription.FromAbovePercentage = moveState.CandidateIntensities[0];
                firstThinPrescription.ProportionalPercentage = moveState.CandidateIntensities[1];
                firstThinPrescription.FromBelowPercentage = moveState.CandidateIntensities[2];
            }
            ThinByPrescription? secondThinPrescription = null;
            if (harvests.Count > 1)
            {
                secondThinPrescription = (ThinByPrescription)harvests[1];
                secondThinPrescription.FromAbovePercentage = moveState.CandidateIntensities[3];
                secondThinPrescription.ProportionalPercentage = moveState.CandidateIntensities[4];
                secondThinPrescription.FromBelowPercentage = moveState.CandidateIntensities[5];
            }
            ThinByPrescription? thirdThinPrescription = null;
            if (harvests.Count > 2)
            {
                thirdThinPrescription = (ThinByPrescription)harvests[2];
                thirdThinPrescription.FromAbovePercentage = moveState.CandidateIntensities[6];
                thirdThinPrescription.ProportionalPercentage = moveState.CandidateIntensities[7];
                thirdThinPrescription.FromBelowPercentage = moveState.CandidateIntensities[8];
            }

            perfCounters.GrowthModelTimesteps += this.CurrentTrajectory.Simulate();

            float candidateFinancialValue = this.GetFinancialValue(this.CurrentTrajectory, moveState.FinancialIndex);
            float acceptedFinancialValue = this.FinancialValue.GetHighestValue();
            float financialValueChange = candidateFinancialValue - acceptedFinancialValue;
            if (financialValueChange > 0.0F)
            {
                // accept change of prescription if it improves upon the best solution
                acceptedFinancialValue = candidateFinancialValue;
                this.CopyTreeGrowthToBestTrajectory(this.CurrentTrajectory);

                if (this.highestFinancialValueMoveLog != null)
                {
                    this.highestFinancialValueMoveLog.SetPrescription(perfCounters.MovesAccepted + perfCounters.MovesRejected, firstThinPrescription, secondThinPrescription, thirdThinPrescription);
                }
                ++perfCounters.MovesAccepted;
            }
            else
            {
                ++perfCounters.MovesRejected;
            }

            this.FinancialValue.AddMove(acceptedFinancialValue, candidateFinancialValue);
            if (this.allMoveLog != null)
            {
                this.allMoveLog.Add(firstThinPrescription, secondThinPrescription, thirdThinPrescription);
            }

            moveState.UpsertRecentlyVisitedPosition();
            return financialValueChange;
        }

        private bool EvaluateGradientMove(MoveState moveState, HeuristicPerformanceCounters perfCounters)
        {
            // synthesize vector in direction of most recent improving gradient with length step size
            int dimensions = moveState.MostRecentIncrementGradient.Length;
            Span<float> netGradient = stackalloc float[dimensions];
            float euclideanFeasibleGradientLength = 0.0F;
            for (int dimension = 0; dimension < dimensions; ++dimension)
            {
                // at least for now, suppress gradients which obviously go beyond constaints
                // Since this zeros out infeasible motions it results in a larger gradient motion in feasible dimensions.
                float netGradientInDimension = moveState.MostRecentIncrementGradient[dimension] - moveState.MostRecentDecrementGradient[dimension];
                float currentPositionInDimension = moveState.ThinningIntensities[dimension];
                if ((netGradientInDimension < 0.0F) && (currentPositionInDimension == 0.0F))
                {
                    continue;
                }
                if ((netGradientInDimension > 0.0F) && (currentPositionInDimension >= moveState.MaximumIntensities[dimension]))
                {
                    continue;
                }

                netGradient[dimension] = netGradientInDimension;
                euclideanFeasibleGradientLength += netGradientInDimension * netGradientInDimension;
            }
            if (euclideanFeasibleGradientLength <= 0.0F)
            {
                Debug.Assert(euclideanFeasibleGradientLength == 0.0F);
                return false; // gradient points into constrained area
            }
            euclideanFeasibleGradientLength = MathF.Sqrt(euclideanFeasibleGradientLength);

            float candidateTotalIntensity = 0.0F;
            float euclideanStepSizeSquared = 0.0F;
            float lengthMultiplier = moveState.StepSize / euclideanFeasibleGradientLength;
            for (int dimension = 0; dimension < dimensions; ++dimension)
            {
                float stepInDimension = lengthMultiplier * netGradient[dimension];
                float candidateIntensityInDimension = moveState.ThinningIntensities[dimension] + stepInDimension;
                if (candidateIntensityInDimension < 0.0F)
                {
                    // since lengthMultiplier is not calculated iteratively, clamping shrinks the gradient step
                    candidateIntensityInDimension = 0.0F;
                    stepInDimension = moveState.ThinningIntensities[dimension];
                }
                else if (candidateIntensityInDimension > moveState.MaximumIntensities[dimension])
                {
                    // also shrinks gradient step size
                    candidateIntensityInDimension = moveState.MaximumIntensities[dimension];
                    stepInDimension = moveState.MaximumIntensities[dimension] - moveState.ThinningIntensities[dimension];
                }

                candidateTotalIntensity += candidateIntensityInDimension;
                moveState.CandidateIntensities[dimension] = candidateIntensityInDimension;
                euclideanStepSizeSquared += stepInDimension * stepInDimension;
            }

            // check if move is feasible within constraints
            if ((candidateTotalIntensity > this.HeuristicParameters.MaximumIntensity) ||
                (candidateTotalIntensity < this.HeuristicParameters.MinimumIntensity))
            {
                return false;
            }

            // evaluate move
            float objectiveFunctionChange = this.EvaluateCandidatePosition(moveState, perfCounters);

            // update or roll back intensities depending on whether move was accepted or rejected
            bool moveAccepted = objectiveFunctionChange > 0.0F;
            if (moveAccepted)
            {
                for (int dimension = 0; dimension < dimensions; ++dimension)
                {
                    moveState.ThinningIntensities[dimension] = moveState.CandidateIntensities[dimension];
                }

                moveState.TotalIntensity = candidateTotalIntensity;
            }
            else
            {
                for (int dimension = 0; dimension < dimensions; ++dimension)
                {
                    moveState.CandidateIntensities[dimension] = moveState.ThinningIntensities[dimension];
                }
            }

            return moveAccepted;
        }

        private bool EvaluateOneDimensionalMove(MoveDirection moveDirection, MoveState moveState, HeuristicPerformanceCounters perfCounters)
        {
            // generate move
            float candidateStepSize = moveState.StepSize;
            float candidateStep;
            if (moveDirection == MoveDirection.Increment)
            {
                float candidateIntensity = moveState.CandidateIntensities[moveState.ActiveDimension] + candidateStepSize;
                if (candidateIntensity > moveState.MaximumIntensities[moveState.ActiveDimension])
                {
                    candidateStepSize = moveState.MaximumIntensities[moveState.ActiveDimension] - moveState.CandidateIntensities[moveState.ActiveDimension];
                }
                if (candidateStepSize < this.HeuristicParameters.MinimumIntensityStepSize)
                {
                    return false;
                }
                candidateStep = candidateStepSize;
            }
            else
            {
                Debug.Assert(moveDirection == MoveDirection.Decrement);
                float candidateIntensity = moveState.CandidateIntensities[moveState.ActiveDimension] - candidateStepSize;
                if (candidateIntensity < 0.0F)
                {
                    candidateStepSize = moveState.CandidateIntensities[moveState.ActiveDimension];
                }
                if (candidateStepSize < this.HeuristicParameters.MinimumIntensityStepSize)
                {
                    return false;
                }
                candidateStep = -candidateStepSize;
            }

            // check if move is feasible within constraints
            float candidateTotalIntensity = moveState.TotalIntensity + candidateStep;
            if ((candidateTotalIntensity > this.HeuristicParameters.MaximumIntensity) ||
                (candidateTotalIntensity < this.HeuristicParameters.MinimumIntensity))
            {
                return false;
            }
            moveState.CandidateIntensities[moveState.ActiveDimension] += candidateStep;

            // evaluate move
            float objectiveFunctionChange = this.EvaluateCandidatePosition(moveState, perfCounters);

            // update or roll back intensities depending on whether move was accepted or rejected
            bool moveAccepted = objectiveFunctionChange > 0.0F;
            if (moveAccepted)
            {
                if (moveState.ActiveDimension >= 0)
                {
                    // basic stochastic testing @ n = 100
                    // ~4% reduction in timesteps from remembering a dimension's previous movement direction
                    // maybe ~1% reduction in timesteps and possibly some disadvantage to repeating steps in the direction of an accepted move
                    // no advantage to combining these two forms of short term memory
                    moveState.MostRecentAcceptedDirection[moveState.ActiveDimension] = moveDirection;
                    // --dimensionIndex; // immediately making another move in the same direction unlikely to provide overall benefit
                    
                    // update most recent accepted dimension to allow checking for and avoiding moving back to previous positions
                    moveState.ThinningIntensities[moveState.ActiveDimension] = moveState.CandidateIntensities[moveState.ActiveDimension];
                }
                moveState.TotalIntensity = candidateTotalIntensity;
            }
            else
            {
                if (moveState.ActiveDimension >= 0)
                {
                    moveState.CandidateIntensities[moveState.ActiveDimension] = moveState.ThinningIntensities[moveState.ActiveDimension];
                }
            }

            // if enabled, update gradient information based on result
            if (this.Gradient)
            {
                float gradient = objectiveFunctionChange / candidateStepSize;
                if (moveDirection == MoveDirection.Increment)
                {
                    moveState.MostRecentIncrementGradient[moveState.ActiveDimension] = gradient;
                }
                else
                {
                    Debug.Assert(moveDirection == MoveDirection.Decrement);
                    moveState.MostRecentDecrementGradient[moveState.ActiveDimension] = gradient;
                }
            }

            return moveAccepted;
        }

        protected override void EvaluateThinningPrescriptions(HeuristicResultPosition position, HeuristicResults results, HeuristicPerformanceCounters perfCounters)
        {
            // initialize search arrays from whatever values are already set on the current trajectory
            // This allows prescriptions to flow between positions in sweeps using GRASP, potentially reducing search effort.
            IList<IHarvest> harvests = this.CurrentTrajectory.Treatments.Harvests;
            MoveState moveState = new(3 * harvests.Count)
            {
                FinancialIndex = position.FinancialIndex,
            };

            for (int harvest = 0; harvest < harvests.Count; ++harvest)
            {
                ThinByPrescription thinPrescription = (ThinByPrescription)harvests[harvest];

                int baseIndex = 3 * harvest;
                moveState.CandidateIntensities[baseIndex] = thinPrescription.FromAbovePercentage;
                moveState.CandidateIntensities[baseIndex + 1] = thinPrescription.ProportionalPercentage;
                moveState.CandidateIntensities[baseIndex + 2] = thinPrescription.FromBelowPercentage;
                moveState.TotalIntensity += thinPrescription.FromAbovePercentage + thinPrescription.ProportionalPercentage + thinPrescription.FromBelowPercentage;

                moveState.ThinningIntensities[baseIndex] = thinPrescription.FromAbovePercentage;
                moveState.ThinningIntensities[baseIndex + 1] = thinPrescription.ProportionalPercentage;
                moveState.ThinningIntensities[baseIndex + 2] = thinPrescription.FromBelowPercentage;

                moveState.MaximumIntensities[baseIndex] = this.HeuristicParameters.FromAbovePercentageUpperLimit;
                moveState.MaximumIntensities[baseIndex + 1] = this.HeuristicParameters.MaximumIntensity;
                moveState.MaximumIntensities[baseIndex + 2] = this.HeuristicParameters.FromBelowPercentageUpperLimit;
            }

            // if no thinning is specified assume, for now, that no elite solution is flowing and the search should begin from a set of
            // balanced proportional thins
            // TODO: fully enroll in regular heuristic initialization and follow construction greediness?
            bool startFromDefaultPosition = false;
            if (moveState.TotalIntensity == 0.0F)
            {
                this.SetDefaultPosition(moveState);
                startFromDefaultPosition = true;
            }

            this.EvaluateCandidatePosition(moveState, perfCounters); // evaluate initial "move"
            if (harvests.Count == 0)
            {
                // no thinning prescriptions to search
                return;
            }

            this.SearchFromPosition(moveState, perfCounters); // look for improving moves in neighborhood

            Debug.Assert(perfCounters.MovesRejected > 0);
            if (this.RestartOnLocalMaximum && (perfCounters.MovesAccepted == 1) && (startFromDefaultPosition == false))
            {
                this.SetDefaultPosition(moveState);
                this.SearchFromPosition(moveState, perfCounters);
            }
        }

        public override string GetName()
        {
            if (this.IsStochastic)
            {
                return "PrescriptionStochasticAscent";
            }
            return "PrescriptionCoordinateAscent";
        }

        private void SearchFromPosition(MoveState moveState, HeuristicPerformanceCounters perfCounters)
        {
            moveState.StepSize = this.HeuristicParameters.DefaultIntensityStepSize;
            while (moveState.StepSize >= this.HeuristicParameters.MinimumIntensityStepSize)
            {
                if (this.IsStochastic)
                {
                    this.Pseudorandom.Shuffle(moveState.Dimensions);
                }

                bool atLeastOneMoveAccepted = false;
                for (int dimensionIndex = 0; dimensionIndex < moveState.Dimensions.Length; ++dimensionIndex)
                {
                    moveState.ActiveDimension = moveState.Dimensions[dimensionIndex];
                    MoveDirection previousDirection = moveState.MostRecentAcceptedDirection[moveState.ActiveDimension];
                    MoveDirection oppositeDirection = previousDirection == MoveDirection.Decrement ? MoveDirection.Increment : MoveDirection.Decrement;
                    if (this.EvaluateOneDimensionalMove(previousDirection, moveState, perfCounters))
                    {
                        atLeastOneMoveAccepted = true;
                    }
                    else if (this.EvaluateOneDimensionalMove(oppositeDirection, moveState, perfCounters))
                    {
                        atLeastOneMoveAccepted = true;
                    }
                }

                // basic stochastic testing @ n = 100
                // ~2% reduction in timesteps from inclusion of a single gradient move per coordinate sweep, ~0.1% increase in financial value, possibly detrimental
                // no clear advantage to repeating gradient moves so long as they're improving (±2% timesteps, ±0.1% financial value, one outlier of 25% reduction in timesteps with 2% reduction in mean financial value observed)
                // no ability to update direction of gradient when repeating gradient moves due to collinearity
                if (this.Gradient && this.EvaluateGradientMove(moveState, perfCounters))
                {
                    atLeastOneMoveAccepted = true;
                }

                if (atLeastOneMoveAccepted == false)
                {
                    // the closer the multiplier is to 1.0 the lower the convergence and, at least as a general principle, the more likely
                    // it is higher quality solutions will be found due to increased search intensity compared to smaller multipliers
                    // Variability in the number of timesteps to 1/ε convergence is substantial but a multiplier of 0.5 appears to be a
                    // reasonable compromise and is seems a common default.
                    moveState.StepSize *= this.HeuristicParameters.StepSizeMultiplier;
                }
            }
        }

        private void SetDefaultPosition(MoveState moveState)
        {
            IList<IHarvest> harvests = this.CurrentTrajectory.Treatments.Harvests;
            float candidateTotalIntensity = 0.0F; // default: no thinnings, so zero thinning intensity
            float initialProportionalIntensity = 0.0F;
            if (harvests.Count > 0)
            {
                candidateTotalIntensity = 0.5F * (this.HeuristicParameters.MaximumIntensity + this.HeuristicParameters.MinimumIntensity);
                initialProportionalIntensity = candidateTotalIntensity / harvests.Count;
            }

            for (int harvest = 0; harvest < harvests.Count; ++harvest)
            {
                int baseIndex = 3 * harvest;
                moveState.CandidateIntensities[baseIndex] = 0.0F;
                moveState.CandidateIntensities[baseIndex + 1] = initialProportionalIntensity;
                moveState.CandidateIntensities[baseIndex + 2] = 0.0F;

                moveState.ThinningIntensities[baseIndex] = 0.0F;
                moveState.ThinningIntensities[baseIndex + 1] = initialProportionalIntensity;
                moveState.ThinningIntensities[baseIndex + 2] = 0.0F;
            }
            moveState.TotalIntensity = candidateTotalIntensity;
        }

        private class MoveState
        {
            private readonly float[][] recentlyVisitedPositions;
            private int recentlyVisitedPositionUpdateIndex;

            public int ActiveDimension { get; set; }
            public float[] CandidateIntensities { get; private init; }
            public int[] Dimensions { get; private init; }
            public int FinancialIndex { get; init; }
            public float[] MaximumIntensities { get; private init; }
            public MoveDirection[] MostRecentAcceptedDirection { get; private init; }
            public float[] MostRecentDecrementGradient { get; private init; }
            public float[] MostRecentIncrementGradient { get; private init; }
            public float StepSize { get; set; }
            public float[] ThinningIntensities { get; set; }
            public float TotalIntensity { get; set; }

            public MoveState(int capacity)
            {
                this.recentlyVisitedPositions = new float[capacity][];
                this.recentlyVisitedPositionUpdateIndex = 0;

                this.ActiveDimension = -1;
                this.CandidateIntensities = new float[capacity];
                this.Dimensions = ArrayExtensions.CreateSequentialIndices(capacity);
                this.FinancialIndex = Constant.HeuristicDefault.FinancialIndex;
                this.MaximumIntensities = new float[capacity];
                this.MostRecentAcceptedDirection = new MoveDirection[capacity]; // defaults to increment
                this.MostRecentDecrementGradient = new float[capacity]; // unused if gradient disabled
                this.MostRecentIncrementGradient = new float[capacity];
                this.StepSize = 0.0F;
                this.ThinningIntensities = new float[capacity];
                this.TotalIntensity = 0.0F;
            }

            public bool CandidatePositionIsRecentlyVisited()
            {
                int positionIndex = this.recentlyVisitedPositionUpdateIndex;
                for (int recentPositionsChecked = 0; recentPositionsChecked < this.recentlyVisitedPositions.Length; ++recentPositionsChecked)
                {
                    float[]? recentlyVisitedPosition = this.recentlyVisitedPositions[this.recentlyVisitedPositionUpdateIndex];
                    if (recentlyVisitedPosition == null)
                    {
                        break;
                    }

                    bool positionMatchesCandidate = true;
                    for (int dimension = 0; dimension < this.CandidateIntensities.Length; ++dimension)
                    {
                        if (this.CandidateIntensities[dimension] != recentlyVisitedPosition[dimension])
                        {
                            positionMatchesCandidate = false;
                            break;
                        }
                    }
                    if (positionMatchesCandidate)
                    {
                        return true;
                    }

                    --positionIndex;
                    if (positionIndex < 0)
                    {
                        positionIndex = this.recentlyVisitedPositions.Length - 1;
                    }
                }

                return false;
            }

            public void UpsertRecentlyVisitedPosition()
            {
                float[]? recentlyVisitedPosition = this.recentlyVisitedPositions[this.recentlyVisitedPositionUpdateIndex];
                if (recentlyVisitedPosition == null)
                {
                    recentlyVisitedPosition = new float[this.CandidateIntensities.Length];
                    this.recentlyVisitedPositions[this.recentlyVisitedPositionUpdateIndex] = recentlyVisitedPosition;
                }
                Array.Copy(this.CandidateIntensities, 0, recentlyVisitedPosition, 0, this.CandidateIntensities.Length);

                ++this.recentlyVisitedPositionUpdateIndex;
                if (this.recentlyVisitedPositionUpdateIndex >= this.recentlyVisitedPositions.Length)
                {
                    this.recentlyVisitedPositionUpdateIndex = 0;
                }
            }
        }
    }
}
