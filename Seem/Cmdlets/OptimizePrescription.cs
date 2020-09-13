﻿using DocumentFormat.OpenXml.Drawing.Diagrams;
using DocumentFormat.OpenXml.Office2013.Excel;
using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommon.Optimize, "Prescription")]
    public class OptimizePrescription : OptimizeCmdlet<PrescriptionParameters>
    {
        [Parameter]
        [ValidateRange(0.0F, 100.0F)]
        public float FromAbovePercentageUpperLimit { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 100.0F)]
        public float FromBelowPercentageUpperLimit { get; set; }

        [Parameter(HelpMessage = "Maximum thinning intensity to evaluate. Paired with minimum intensities rather than used combinatorially.")]
        [ValidateRange(0.0F, 1000.0F)]
        public List<float> Maximum { get; set; }

        [Parameter(HelpMessage = "Minimum thinning intensity to evaluate. Paired with maximum intensities rather than used combinatorially.")]
        [ValidateRange(0.0F, 1000.0F)]
        public List<float> Minimum { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 100.0F)]
        public float ProportionalPercentageUpperLimit { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 100.0F)]
        public float Step { get; set; }

        [Parameter]
        public PrescriptionUnits Units { get; set; }

        public OptimizePrescription()
        {
            this.Cores = 1;
            this.FromAbovePercentageUpperLimit = 100.0F;
            this.FromBelowPercentageUpperLimit = 100.0F;
            this.Maximum = new List<float>() { Constant.PrescriptionEnumerationDefault.MaximumIntensity };
            this.Minimum = new List<float>() { Constant.PrescriptionEnumerationDefault.MinimumIntensity };
            this.PerturbBy = 0.0F;
            this.ProportionalPercentage[0] = 0.0F;
            this.ProportionalPercentageUpperLimit = 100.0F;
            this.Step = Constant.PrescriptionEnumerationDefault.IntensityStep;
            this.Units = Constant.PrescriptionEnumerationDefault.Units;
        }

        protected override IHarvest CreateHarvest(int harvestPeriodIndex)
        {
            return new ThinByPrescription(this.HarvestPeriods[harvestPeriodIndex]);
        }

        protected override Heuristic CreateHeuristic(OrganonConfiguration organonConfiguration, Objective objective, PrescriptionParameters parameters)
        {
            return new PrescriptionEnumeration(this.Stand, organonConfiguration, objective, parameters);
        }

        protected override string GetName()
        {
            return "Optimize-Prescription";
        }

        protected override IList<PrescriptionParameters> GetParameterCombinations()
        {
            if (this.Cores != 1)
            {
                throw new NotSupportedException();
            }
            if (this.Minimum.Count != this.Maximum.Count)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (this.PerturbBy != 0.0F)
            {
                throw new NotSupportedException();
            }
            if ((this.ProportionalPercentage.Count != 1) || (this.ProportionalPercentage[0] != 0.0F))
            {
                throw new NotSupportedException();
            }
            if (this.Step < 0.0F)
            {
                throw new ArgumentOutOfRangeException(nameof(this.Step));
            }

            List<PrescriptionParameters> parameters = new List<PrescriptionParameters>(this.Minimum.Count);
            for (int intensityIndex = 0; intensityIndex < this.Minimum.Count; ++intensityIndex)
            {
                float minimumIntensity = this.Minimum[intensityIndex];
                float maximumIntensity = this.Maximum[intensityIndex];
                if (maximumIntensity < minimumIntensity)
                {
                    throw new ArgumentOutOfRangeException();
                }

                parameters.Add(new PrescriptionParameters()
                {
                    FromAbovePercentageUpperLimit = this.FromAbovePercentageUpperLimit,
                    FromBelowPercentageUpperLimit = this.FromBelowPercentageUpperLimit,
                    Minimum = minimumIntensity,
                    Maximum = maximumIntensity,
                    ProportionalPercentageUpperLimit = this.ProportionalPercentageUpperLimit,
                    Step = this.Step,
                    TimberValue = this.TimberValue,
                    Units = this.Units,
                    UseScaledVolume = this.ScaledVolume
                });
            }
            return parameters;
        }
    }
}