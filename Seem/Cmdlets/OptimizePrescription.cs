using DocumentFormat.OpenXml.Drawing.Diagrams;
using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommon.Optimize, "Prescription")]
    public class OptimizePrescription : OptimizeCmdlet<PrescriptionParameters>
    {
        [Parameter(HelpMessage = "Step size, in percent, of above, proportional, and below percentages of first thinning prescription. If present, a second or third thinning's step size is scaled to account for trees removed in the first thinning.")]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0F, 100.0F)]
        public List<float> DefaultStep { get; set; }

        [Parameter]
        public SwitchParameter Enumerate { get; set; }
        [Parameter(HelpMessage = "Ignored if -Enumerate is set.")]
        public SwitchParameter Gradient { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 100.0F)]
        public float FromAbovePercentageUpperLimit { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 100.0F)]
        public float FromBelowPercentageUpperLimit { get; set; }

        [Parameter]
        public SwitchParameter LogAllMoves { get; set; }

        [Parameter(HelpMessage = "Maximum thinning intensity to evaluate. Paired with minimum intensities rather than used combinatorially.")]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0F, 1000.0F)]
        public List<float> MaximumIntensity { get; set; }
        [Parameter(HelpMessage = "Maximum step size, in percent, of above, proportional, and below percentages.")]
        [ValidateRange(0.0F, 100.0F)]
        public float MaximumStep { get; set; }

        [Parameter(HelpMessage = "Minimum thinning intensity to evaluate. Paired with maximum intensities rather than used combinatorially.")]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0F, 1000.0F)]
        public List<float> MinimumIntensity { get; set; }
        [Parameter(HelpMessage = "Minimum step size, in percent, of above, proportional, and below percentages.")]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0F, 100.0F)]
        public List<float> MinimumStep { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 100.0F)]
        public float ProportionalPercentageUpperLimit { get; set; }
        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0F, 1.0F)]
        public List<float> StepMultiplier { get; set; }

        [Parameter(HelpMessage = "Ignored if -Enumerate is set. Recommended to set to false when -BestOf is more than 2-4 to avoid redundant checking.")]
        public SwitchParameter RestartOnLocalMaximum { get; set; }

        [Parameter(HelpMessage = "Ignored if -Enumerate is set.")]
        public SwitchParameter Stochastic { get; set; }

        [Parameter]
        public PrescriptionUnits Units { get; set; }

        public OptimizePrescription()
        {
            Debug.Assert(this.InitialThinningProbability.Count == 1);

            this.ConstructionGreediness = new() { Constant.Grasp.FullyGreedyConstructionForMaximization };
            this.DefaultStep = new() { Constant.PrescriptionSearchDefault.DefaultIntensityStepSize };
            this.FromAbovePercentageUpperLimit = 100.0F;
            this.FromBelowPercentageUpperLimit = 100.0F;
            this.Gradient = false;
            this.InitialThinningProbability[0] = 0.0F;
            this.LogAllMoves = false;
            this.MaximumIntensity = new() { Constant.PrescriptionSearchDefault.MaximumIntensity };
            this.MinimumIntensity = new() { Constant.PrescriptionSearchDefault.MinimumIntensity };
            this.MaximumStep = Constant.PrescriptionSearchDefault.MaximumIntensityStepSize;
            this.MinimumStep = new() { Constant.PrescriptionSearchDefault.MinimumIntensityStepSize };
            this.ProportionalPercentageUpperLimit = 100.0F;
            // don't change this.SolutionPoolSize as stochastic coordinate descent is the default
            this.RestartOnLocalMaximum = true; // mitigates risk of entrapment
            this.StepMultiplier = new() { Constant.PrescriptionSearchDefault.StepSizeMultiplier };
            this.Stochastic = false; // ~13% less efficient than defaulting true in simple testing
            this.Units = Constant.PrescriptionSearchDefault.Units;
        }

        protected override bool HeuristicEvaluatesAcrossPlanningPeriodsAndDiscountRates
        {
            get { return this.Enumerate; }
        }

        protected override Heuristic<PrescriptionParameters> CreateHeuristic(PrescriptionParameters heuristicParameters, RunParameters runParameters)
        {
            if (this.Enumerate)
            {
                if (this.BestOf != 1)
                {
                    throw new NotSupportedException(nameof(this.BestOf)); // enumeration is deterministic, so no value in repeated runs
                }
                // ignore this.RestartOnLocalMaximum and this.Stochastic
                return new PrescriptionEnumeration(this.Stand!, heuristicParameters, runParameters);
            }
            else
            {
                // in general, this.Stochastic = true makes the most sense with this.BestOf > 1 but race conditions between threads create
                // nondeterministic behavior with non-stochastic searches due to changes in the order of position execution and, thus,
                // solution construction
                return new PrescriptionCoordinateAscent(this.Stand!, heuristicParameters, runParameters)
                {
                    Gradient = this.Gradient,
                    IsStochastic = this.Stochastic,
                    RestartOnLocalMaximum = this.RestartOnLocalMaximum
                };
            }
        }

        protected override IHarvest CreateThin(int thinPeriodIndex)
        {
            return new ThinByPrescription(thinPeriodIndex);
        }

        protected override string GetName()
        {
            return "Optimize-Prescription";
        }

        protected override IList<PrescriptionParameters> GetParameterCombinations()
        {
            if ((this.ConstructionGreediness.Count != 1) || (this.ConstructionGreediness[0] != Constant.Grasp.FullyGreedyConstructionForMaximization))
            {
                throw new ParameterOutOfRangeException(nameof(this.ConstructionGreediness));
            }
            if (this.DefaultStep.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.DefaultStep));
            }
            if ((this.FromAbovePercentageUpperLimit < 0.0F) || (this.FromAbovePercentageUpperLimit > 100.0F))
            {
                throw new ParameterOutOfRangeException(nameof(this.FromAbovePercentageUpperLimit));
            }
            if ((this.FromBelowPercentageUpperLimit < 0.0F) || (this.FromBelowPercentageUpperLimit > 100.0F))
            {
                throw new ParameterOutOfRangeException(nameof(this.FromBelowPercentageUpperLimit));
            }
            if ((this.InitialThinningProbability.Count != 1) || (this.InitialThinningProbability[0] != 0.0F))
            {
                throw new ParameterOutOfRangeException(nameof(this.InitialThinningProbability));
            }
            if (this.MaximumIntensity.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaximumIntensity));
            }
            if ((this.MaximumStep <= 0.0F) || (this.MaximumStep > 100.0F))
            {
                throw new ParameterOutOfRangeException(nameof(this.MaximumStep));
            }
            if ((this.MinimumIntensity.Count < 1) || (this.MinimumIntensity.Count != this.MaximumIntensity.Count))
            {
                throw new ParameterOutOfRangeException(nameof(this.MinimumIntensity));
            }
            if (this.MinimumStep.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.MinimumStep));
            }
            if ((this.ProportionalPercentageUpperLimit < 0.0F) || (this.ProportionalPercentageUpperLimit > 100.0F))
            {
                throw new ParameterOutOfRangeException(nameof(this.ProportionalPercentageUpperLimit));
            }
            if (this.StepMultiplier.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.StepMultiplier));
            }

            List<PrescriptionParameters> parameterCombinations = new(this.MinimumIntensity.Count);
            for (int minimumStepIndex = 0; minimumStepIndex < this.MinimumStep.Count; ++minimumStepIndex)
            {
                float minimumStepSize = this.MinimumStep[minimumStepIndex];
                if ((minimumStepSize <= 0.0F) || (minimumStepSize > this.MaximumStep))
                {
                    throw new ParameterOutOfRangeException(nameof(this.MinimumStep));
                }

                for (int defaultStepSizeIndex = 0; defaultStepSizeIndex < this.DefaultStep.Count; ++defaultStepSizeIndex)
                {
                    float defaultStepSize = this.DefaultStep[defaultStepSizeIndex];
                    if ((defaultStepSize <= 0.0F) || (defaultStepSize > this.MaximumStep))
                    {
                        throw new ParameterOutOfRangeException(nameof(this.DefaultStep));
                    }
                    if (defaultStepSize < minimumStepSize)
                    {
                        // skip invalid combinations of default step and minimum step sizes
                        continue;
                    }

                    for (int stepMultiplierIndex = 0; stepMultiplierIndex < this.StepMultiplier.Count; ++stepMultiplierIndex)
                    {
                        float stepSizeMultiplier = this.StepMultiplier[stepMultiplierIndex];
                        if ((stepSizeMultiplier < 0.0F) || (stepSizeMultiplier >= 1.0F))
                        {
                            throw new ParameterOutOfRangeException(nameof(this.StepMultiplier));
                        }

                        for (int intensityRangeIndex = 0; intensityRangeIndex < this.MinimumIntensity.Count; ++intensityRangeIndex)
                        {
                            float minimumIntensity = this.MinimumIntensity[intensityRangeIndex];
                            float maximumIntensity = this.MaximumIntensity[intensityRangeIndex];
                            if ((minimumIntensity < 0.0F) || (maximumIntensity < minimumIntensity))
                            {
                                throw new ParameterOutOfRangeException(nameof(this.MinimumIntensity));
                            }

                            parameterCombinations.Add(new PrescriptionParameters()
                            {
                                DefaultIntensityStepSize = defaultStepSize,
                                FromAbovePercentageUpperLimit = this.FromAbovePercentageUpperLimit,
                                FromBelowPercentageUpperLimit = this.FromBelowPercentageUpperLimit,
                                LogAllMoves = this.LogAllMoves,
                                MaximumIntensity = maximumIntensity,
                                MaximumIntensityStepSize = this.MaximumStep,
                                MinimumIntensity = minimumIntensity,
                                MinimumIntensityStepSize = minimumStepSize,
                                ProportionalPercentageUpperLimit = this.ProportionalPercentageUpperLimit,
                                StepSizeMultiplier = stepSizeMultiplier,
                                Units = this.Units
                            });
                        }
                    }
                }
            }
            return parameterCombinations;
        }
    }
}
