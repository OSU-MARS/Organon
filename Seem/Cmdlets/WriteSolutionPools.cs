using Osu.Cof.Ferm.Heuristics;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommunications.Write, "SolutionPools")]
    public class WriteSolutionPools : WriteCmdlet
    {
        [Parameter]
        [ValidateNotNull]
        public HeuristicResults? Results { get; set; }

        protected override void ProcessRecord()
        {
            int poolCapacity = 0;
            if (this.Results!.PositionsEvaluated.Count > 0)
            {
                HeuristicResultPosition position = this.Results.PositionsEvaluated[0];
                poolCapacity = this.Results[position].Pool.PoolCapacity;
            }

            using StreamWriter writer = this.GetWriter();

            // header
            bool resultsSpecified = this.Results != null;
            if (this.ShouldWriteHeader())
            {
                string?[] financialHeader = new string?[poolCapacity];
                string?[] distanceHeader = new string?[poolCapacity];
                for (int solutionIndex = 0; solutionIndex < poolCapacity; ++solutionIndex)
                {
                    financialHeader[solutionIndex] = "financial" + solutionIndex.ToString(CultureInfo.InvariantCulture);
                    distanceHeader[solutionIndex] = "distance" + solutionIndex.ToString(CultureInfo.InvariantCulture);
                }

                writer.WriteLine(WriteCmdlet.GetHeuristicAndPositionCsvHeader(this.Results) + ",pooled,accepted,rejected," + 
                                 String.Join(',', financialHeader) + "," +
                                 String.Join(',', distanceHeader));
            }

            // pool contents
            long maxFileSizeInBytes = this.GetMaxFileSizeInBytes();
            string?[] financialValues = new string?[poolCapacity];
            string?[] distances = new string?[poolCapacity];
            for (int resultIndex = 0; resultIndex < this.Results!.PositionsEvaluated.Count; ++resultIndex)
            {
                HeuristicResultPosition position = this.Results.PositionsEvaluated[resultIndex];
                HeuristicSolutionPool solutions = this.Results[position].Pool;
                if (solutions.PoolCapacity != poolCapacity)
                {
                    throw new NotSupportedException("Solution pool capacity changed from " + poolCapacity + " to " + solutions.PoolCapacity + ".");
                }

                string heuristicAndPosition = WriteCmdlet.GetHeuristicAndPositionCsvValues(solutions, this.Results, position);
                for (int solutionIndex = 0; solutionIndex < solutions.SolutionsInPool; ++solutionIndex)
                {
                    Heuristic? eliteSolution = solutions.EliteSolutions[solutionIndex];
                    Debug.Assert(eliteSolution != null);
                    float highestFinancialValue = eliteSolution.FinancialValue.GetHighestValueWithDefaulting(position);
                    int nearestNeighborDistance = SolutionPool.UnknownDistance;
                    int nearestNeighborIndex = solutions.NearestNeighborIndex[solutionIndex];
                    if (nearestNeighborIndex != SolutionPool.UnknownNeighbor)
                    {
                        nearestNeighborDistance = solutions.DistanceMatrix[nearestNeighborIndex, solutionIndex];
                        Debug.Assert((nearestNeighborIndex != solutionIndex) && (nearestNeighborDistance == solutions.DistanceMatrix[solutionIndex, nearestNeighborIndex]));
                    }

                    financialValues[solutionIndex] = highestFinancialValue.ToString(CultureInfo.InvariantCulture);
                    if (nearestNeighborDistance != SolutionPool.UnknownDistance)
                    {
                        distances[solutionIndex] = nearestNeighborDistance.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        distances[solutionIndex] = null;
                    }
                }
                for (int solutionIndex = solutions.SolutionsInPool; solutionIndex < financialValues.Length; ++solutionIndex)
                {
                    financialValues[solutionIndex] = null;
                    distances[solutionIndex] = null;
                }

                writer.WriteLine(heuristicAndPosition + "," +
                                 solutions.SolutionsInPool.ToString(CultureInfo.InvariantCulture) + "," +
                                 solutions.SolutionsAccepted.ToString(CultureInfo.InvariantCulture) + "," +
                                 solutions.SolutionsRejected.ToString(CultureInfo.InvariantCulture) + "," +
                                 String.Join(',', financialValues) + "," + 
                                 String.Join(',', distances));

                if (writer.BaseStream.Length > maxFileSizeInBytes)
                {
                    this.WriteWarning("Write-SolutionPool: File size limit of " + this.LimitGB.ToString("0.00") + " GB exceeded.");
                    break;
                }
            }
        }
    }
}
