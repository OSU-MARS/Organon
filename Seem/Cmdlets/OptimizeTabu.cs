﻿using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommon.Optimize, "Tabu")]
    public class OptimizeTabu : OptimizeCmdlet<TabuParameters>
    {
        [Parameter]
        [ValidateRange(0.0F, 1.0F)]
        public List<float> EscapeAfter { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 1.0F)]
        public List<float> EscapeBy { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 10.0F)]
        public List<float> IterationMultipliers { get; set; }

        //[Parameter]
        //[ValidateRange(1, 100)]
        //public int? Jump { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 1.0F)]
        public List<float> MaxTenure { get; set; }

        [Parameter]
        public TabuTenure Tenure { get; set; }

        public OptimizeTabu()
        {
            this.EscapeAfter = new List<float>() { Constant.TabuDefault.EscapeAfter };
            this.EscapeBy = new List<float>() { Constant.TabuDefault.EscapeBy };
            this.IterationMultipliers = new List<float>() { Constant.TabuDefault.IterationMultiplier };
            this.MaxTenure = new List<float>() { Constant.TabuDefault.MaximumTenureRatio };
            this.Tenure = Constant.TabuDefault.Tenure;
        }

        protected override Heuristic CreateHeuristic(OrganonConfiguration organonConfiguration, Objective objective, TabuParameters parameters)
        {
            return new TabuSearch(this.Stand!, organonConfiguration, objective, parameters);
        }

        protected override string GetName()
        {
            return "Optimize-Tabu";
        }

        protected override IList<TabuParameters> GetParameterCombinations(TimberValue timberValue)
        {
            int treeRecords = this.Stand!.GetTreeRecordCount();

            List<TabuParameters> parameterCombinations = new(this.EscapeAfter.Count * this.EscapeBy.Count *
                this.IterationMultipliers.Count * this.MaxTenure.Count * this.ProportionalPercentage.Count);
            foreach (float escapeAfter in this.EscapeAfter)
            {
                foreach (float escapeBy in this.EscapeBy)
                {
                    foreach (float iterationRatio in this.IterationMultipliers)
                    {
                        foreach (float tenureRatio in this.MaxTenure)
                        {
                            foreach (float proportionalPercentage in this.ProportionalPercentage)
                            {
                                parameterCombinations.Add(new TabuParameters()
                                {
                                    EscapeAfter = (int)(escapeAfter * treeRecords),
                                    EscapeDistance = (int)(escapeBy * treeRecords),
                                    Iterations = (int)(iterationRatio * treeRecords / MathF.Log(treeRecords) + 0.5F),
                                    MaximumTenure = (int)(tenureRatio * treeRecords),
                                    PerturbBy = this.PerturbBy,
                                    ProportionalPercentage = proportionalPercentage,
                                    Tenure = this.Tenure,
                                    TimberValue = timberValue,
                                    UseFiaVolume = this.ScaledVolume
                                });
                            }
                        }
                    }
                }
            }
            return parameterCombinations;
        }
    }
}
