using Osu.Cof.Ferm.Heuristics;
using System;
using System.Globalization;
using System.IO;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommunications.Write, "Population")]
    public class WritePopulation : WriteHeuristicResultsCmdlet
    {
        protected override void ProcessRecord()
        {
            if (this.Results!.PositionsEvaluated.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.Results));
            }

            using StreamWriter writer = this.GetWriter();

            if (this.ShouldWriteHeader())
            {
                writer.WriteLine(WriteCmdlet.GetHeuristicAndPositionCsvHeader(this.Results) + ",generation,highMin,highMean,highMax,highCov,highAlleles,highHeterozygosity,highIndividuals,highPolymorphism,lowMin,lowMean,lowMax,lowCov,lowAlleles,lowHeterozygosity,lowIndividuals,lowPolymorphism");
            }

            long maxFileSizeInBytes = this.GetMaxFileSizeInBytes();
            for (int positionIndex = 0; positionIndex < this.Results.PositionsEvaluated.Count; ++positionIndex)
            {
                HeuristicResultPosition position = this.Results.PositionsEvaluated[positionIndex];
                HeuristicResult result = this.Results[position];
                string heuristicAndPosition = WriteCmdlet.GetHeuristicAndPositionCsvValues(result.Pool, this.Results, position);
                PopulationStatistics highStatistics = ((GeneticAlgorithm)result.Pool.High!).PopulationStatistics;
                PopulationStatistics lowStatistics = ((GeneticAlgorithm)result.Pool.Low!).PopulationStatistics;
                int maxGenerations = Math.Max(highStatistics.Generations, lowStatistics.Generations);
                for (int generationIndex = 0; generationIndex < maxGenerations; ++generationIndex)
                {
                    string? highMinimumFitness = null;
                    string? highMeanFitness = null;
                    string? highMaximumFitness = null;
                    string? highCoefficientOfVariance = null;
                    string? highMeanAlleles = null;
                    string? highMeanHeterozygosity = null;
                    string? highNewIndividuals = null;
                    string? highPolymorphism = null;
                    if (highStatistics.Generations > generationIndex)
                    {
                        highMinimumFitness = highStatistics.MinimumFitnessByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        highMeanFitness = highStatistics.MeanFitnessByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        highMaximumFitness = highStatistics.MaximumFitnessByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        highCoefficientOfVariance = highStatistics.CoefficientOfVarianceByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        highMeanAlleles = highStatistics.MeanAllelesPerLocusByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        highMeanHeterozygosity = highStatistics.MeanHeterozygosityByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        highNewIndividuals = highStatistics.NewIndividualsByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        highPolymorphism = highStatistics.PolymorphismByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                    }

                    string? lowMinimumFitness = null;
                    string? lowMeanFitness = null;
                    string? lowMaximumFitness = null;
                    string? lowCoefficientOfVariance = null;
                    string? lowMeanAlleles = null;
                    string? lowMeanHeterozygosity = null;
                    string? lowNewIndividuals = null;
                    string? lowPolymorphism = null;
                    if (lowStatistics.Generations > generationIndex)
                    {
                        lowMinimumFitness = lowStatistics.MinimumFitnessByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        lowMeanFitness = lowStatistics.MeanFitnessByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        lowMaximumFitness = lowStatistics.MaximumFitnessByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        lowCoefficientOfVariance = lowStatistics.CoefficientOfVarianceByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        lowMeanAlleles = lowStatistics.MeanAllelesPerLocusByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        lowMeanHeterozygosity = lowStatistics.MeanHeterozygosityByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        lowNewIndividuals = lowStatistics.NewIndividualsByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                        lowPolymorphism = lowStatistics.PolymorphismByGeneration[generationIndex].ToString(CultureInfo.InvariantCulture);
                    }

                    writer.WriteLine(heuristicAndPosition + "," +
                                     generationIndex + "," +
                                     highMinimumFitness + "," +
                                     highMeanFitness + "," +
                                     highMaximumFitness + "," +
                                     highCoefficientOfVariance + "," +
                                     highMeanAlleles + "," +
                                     highMeanHeterozygosity + "," +
                                     highNewIndividuals + "," +
                                     highPolymorphism + "," +
                                     lowMinimumFitness + "," +
                                     lowMeanFitness + "," +
                                     lowMaximumFitness + "," +
                                     lowCoefficientOfVariance + "," +
                                     lowMeanAlleles + "," +
                                     lowMeanHeterozygosity + "," +
                                     lowNewIndividuals + "," +
                                     lowPolymorphism);
                }

                if (writer.BaseStream.Length > maxFileSizeInBytes)
                {
                    this.WriteWarning("Write-Population: File size limit of " + this.LimitGB.ToString("0.00") + " GB exceeded.");
                    break;
                }
            }
        }
    }
}
