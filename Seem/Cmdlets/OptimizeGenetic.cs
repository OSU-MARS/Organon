using Osu.Cof.Ferm.Heuristics;
using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommon.Optimize, "Genetic")]
    public class OptimizeGenetic : OptimizeCmdlet<GeneticParameters>
    {
        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0, 1.0)]
        public List<float> CrossoverProbabilityEnd { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0, 1.0)]
        public List<float> ExchangeProbabilityEnd { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(-20.0, 0.0)]
        public List<float> ExponentK { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0, 10.0)]
        public List<float> FlipProbabilityEnd { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0, Single.MaxValue)]
        public List<float> GenerationMultiplier { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(1, Int32.MaxValue)]
        public List<int> InitializationClasses { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        public List<PopulationInitializationMethod> InitializationMethod { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0, Single.MaxValue)]
        public List<float> MinCoefficientOfVariation { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(1, Int32.MaxValue)]
        public List<int> PopulationSize { get; set; }

        [Parameter]
        public PopulationReplacementStrategy ReplacementStrategy { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0.0, 1.0)]
        public List<float> ReservedProportion { get; set; }

        public OptimizeGenetic()
        {
            this.CrossoverProbabilityEnd = new() { Constant.GeneticDefault.CrossoverProbabilityEnd };
            this.ExchangeProbabilityEnd = new() { Constant.GeneticDefault.ExchangeProbabilityEnd };
            this.ExponentK = new() { Constant.GeneticDefault.ExponentK };
            this.FlipProbabilityEnd = new() { Constant.GeneticDefault.FlipProbabilityEnd };
            this.GenerationMultiplier = new() { Constant.GeneticDefault.GenerationMultiplier };
            this.InitializationClasses = new() { Constant.GeneticDefault.InitializationClasses };
            this.InitializationMethod = new() { Constant.GeneticDefault.InitializationMethod };
            this.MinCoefficientOfVariation = new() { Constant.GeneticDefault.MinimumCoefficientOfVariation };
            this.PopulationSize = new() { Constant.GeneticDefault.PopulationSize };
            this.ReplacementStrategy = Constant.GeneticDefault.ReplacementStrategy;
            this.ReservedProportion = new() { Constant.GeneticDefault.ReservedPopulationProportion };
        }

        protected override Heuristic<GeneticParameters> CreateHeuristic(GeneticParameters heuristicParameters, RunParameters runParameters)
        {
            return new GeneticAlgorithm(this.Stand!, heuristicParameters, runParameters);
        }

        protected override string GetName()
        {
            return "Optimize-Genetic";
        }

        protected override IList<GeneticParameters> GetParameterCombinations()
        {
            if (this.ExchangeProbabilityEnd.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.ExchangeProbabilityEnd));
            }
            if (this.ExponentK.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.ExponentK));
            }
            if (this.FlipProbabilityEnd.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.FlipProbabilityEnd));
            }
            if (this.GenerationMultiplier.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.GenerationMultiplier));
            }
            if (this.InitializationClasses.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.InitializationClasses));
            }
            if (this.MinCoefficientOfVariation.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.MinCoefficientOfVariation));
            }
            if (this.PopulationSize.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.PopulationSize));
            }
            if (this.ReservedProportion.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.ReservedProportion));
            }

            List<GeneticParameters> parameterCombinations = new(this.CrossoverProbabilityEnd.Count * this.ExponentK.Count * 
                this.FlipProbabilityEnd.Count * this.GenerationMultiplier.Count * this.InitializationMethod.Count * this.InitializationClasses.Count * 
                this.MinCoefficientOfVariation.Count * this.PopulationSize.Count * this.InitialThinningProbability.Count * this.ReservedProportion.Count);
            int treeRecordCount = this.Stand!.GetTreeRecordCount();
            foreach (float crossoverProbabilityEnd in this.CrossoverProbabilityEnd)
            {
                foreach (float exponent in this.ExponentK)
                {
                    foreach (float exchangeProbabilityEnd in this.ExchangeProbabilityEnd)
                    {
                        foreach (float flipProbabilityEnd in this.FlipProbabilityEnd)
                        {
                            foreach (float generationMultiplier in this.GenerationMultiplier)
                            {
                                foreach (PopulationInitializationMethod initializationMethod in this.InitializationMethod)
                                {
                                    foreach (int initializationClassCount in this.InitializationClasses)
                                    {
                                        foreach (float minCoefficientOfVariation in this.MinCoefficientOfVariation)
                                        {
                                            foreach (int populationSize in this.PopulationSize)
                                            {
                                                foreach (float reservedProportion in this.ReservedProportion)
                                                {
                                                    foreach (float constructionGreediness in this.ConstructionGreediness)
                                                    {
                                                        foreach (float thinningProbability in this.InitialThinningProbability)
                                                        {
                                                            parameterCombinations.Add(new GeneticParameters()
                                                            {
                                                                MinimumConstructionGreediness = constructionGreediness,
                                                                CrossoverProbabilityEnd = crossoverProbabilityEnd,
                                                                ExchangeProbabilityEnd = exchangeProbabilityEnd,
                                                                ExchangeProbabilityStart = Constant.GeneticDefault.ExchangeProbabilityStart,
                                                                ExponentK = exponent,
                                                                FlipProbabilityEnd = flipProbabilityEnd,
                                                                FlipProbabilityStart = Constant.GeneticDefault.FlipProbabilityStart,
                                                                InitializationClasses = initializationClassCount,
                                                                InitializationMethod = initializationMethod,
                                                                MaximumGenerations = (int)(generationMultiplier * MathF.Pow(treeRecordCount, Constant.GeneticDefault.GenerationPower) + 0.5F),
                                                                MinimumCoefficientOfVariation = minCoefficientOfVariation,
                                                                PopulationSize = populationSize,
                                                                InitialThinningProbability = thinningProbability,
                                                                ReplacementStrategy = this.ReplacementStrategy,
                                                                ReservedProportion = reservedProportion
                                                            });
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
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
