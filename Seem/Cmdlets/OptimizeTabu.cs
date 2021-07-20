using Osu.Cof.Ferm.Heuristics;
using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommon.Optimize, "Tabu")]
    public class OptimizeTabu : OptimizeCmdlet<TabuParameters>
    {
        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0F, 1.0F)]
        public List<float> EscapeAfter { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0F, 1.0F)]
        public List<float> EscapeBy { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0F, 10.0F)]
        public List<float> IterationMultipliers { get; set; }

        //[Parameter]
        //[ValidateRange(1, 100)]
        //public int? Jump { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0F, 1.0F)]
        public List<float> MaxTenure { get; set; }

        [Parameter]
        public TabuTenure Tenure { get; set; }

        public OptimizeTabu()
        {
            this.EscapeAfter = new() { Constant.TabuDefault.EscapeAfter };
            this.EscapeBy = new() { Constant.TabuDefault.EscapeBy };
            this.IterationMultipliers = new() { Constant.TabuDefault.IterationMultiplier };
            this.MaxTenure = new() { Constant.TabuDefault.MaximumTenureRatio };
            this.Tenure = Constant.TabuDefault.Tenure;
        }

        protected override Heuristic<TabuParameters> CreateHeuristic(TabuParameters heuristicParameters, RunParameters runParameters)
        {
            return new TabuSearch(this.Stand!, heuristicParameters, runParameters);
        }

        protected override string GetName()
        {
            return "Optimize-Tabu";
        }

        protected override IList<TabuParameters> GetParameterCombinations()
        {
            int treeRecords = this.Stand!.GetTreeRecordCount();

            List<TabuParameters> parameterCombinations = new(this.EscapeAfter.Count * this.EscapeBy.Count *
                this.IterationMultipliers.Count * this.MaxTenure.Count * this.InitialThinningProbability.Count);
            foreach (float escapeAfter in this.EscapeAfter)
            {
                foreach (float escapeBy in this.EscapeBy)
                {
                    foreach (float iterationRatio in this.IterationMultipliers)
                    {
                        foreach (float tenureRatio in this.MaxTenure)
                        {
                            foreach (float constructionGreediness in this.ConstructionGreediness)
                            {
                                foreach (float thinningProbability in this.InitialThinningProbability)
                                {
                                    parameterCombinations.Add(new TabuParameters()
                                    {
                                        EscapeAfter = (int)(escapeAfter * treeRecords),
                                        EscapeDistance = (int)(escapeBy * treeRecords),
                                        Iterations = (int)(iterationRatio * treeRecords / MathF.Log(treeRecords) + 0.5F),
                                        MaximumTenure = (int)(tenureRatio * treeRecords),
                                        MinimumConstructionGreediness = constructionGreediness,
                                        InitialThinningProbability = thinningProbability,
                                        Tenure = this.Tenure
                                    });
                                }
                            }
                        }
                    }
                }
            }
            return parameterCombinations;
        }
    }
}
