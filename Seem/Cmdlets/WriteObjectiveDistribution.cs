using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommunications.Write, "ObjectiveDistribution")]
    public class WriteObjectiveDistribution : WriteHeuristicResultsCmdlet
    {
        protected override void ProcessRecord()
        {
            Debug.Assert(this.Results != null);
            if (this.Results.PositionsEvaluated.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.Results));
            }

            using StreamWriter writer = this.GetWriter();

            if (this.ShouldWriteHeader())
            {
                writer.WriteLine(WriteCmdlet.GetHeuristicAndPositionCsvHeader(this.Results) + ",solution,objective,moveAccepted,movesRejected,runtime,timesteps,treesRandomized");
            }

            long maxFileSizeInBytes = this.GetMaxFileSizeInBytes();
            for (int positionIndex = 0; positionIndex < this.Results.PositionsEvaluated.Count; ++positionIndex)
            {
                HeuristicResultPosition position = this.Results.PositionsEvaluated[positionIndex];
                HeuristicResult result = this.Results[position];
                string heuristicAndPosition = WriteCmdlet.GetHeuristicAndPositionCsvValues(result.Pool, this.Results, position);
                Heuristic highHeuristic = result.Pool.High!;
                OrganonStandTrajectory highTrajectory = highHeuristic.GetBestTrajectoryWithDefaulting(position);

                HeuristicObjectiveDistribution distribution = result.Distribution;
                List<float> highestFinancialValues = distribution.HighestFinancialValueBySolution;
                for (int solutionIndex = 0; solutionIndex < highestFinancialValues.Count; ++solutionIndex)
                {
                    HeuristicPerformanceCounters perfCounters = distribution.PerfCountersBySolution[solutionIndex];
                    writer.WriteLine(heuristicAndPosition + "," +
                                     solutionIndex.ToString(CultureInfo.InvariantCulture) + "," +
                                     highestFinancialValues[solutionIndex].ToString(CultureInfo.InvariantCulture) + "," +
                                     perfCounters.MovesAccepted.ToString(CultureInfo.InvariantCulture) + "," +
                                     perfCounters.MovesRejected.ToString(CultureInfo.InvariantCulture) + "," +
                                     perfCounters.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                                     perfCounters.GrowthModelTimesteps.ToString(CultureInfo.InvariantCulture) + "," +
                                     perfCounters.TreesRandomizedInConstruction.ToString(CultureInfo.InvariantCulture));
                }

                if (writer.BaseStream.Length > maxFileSizeInBytes)
                {
                    this.WriteWarning("Write-ObjectiveDistribution: File size limit of " + this.LimitGB.ToString("0.00") + " GB exceeded.");
                    break;
                }
            }
        }
    }
}
