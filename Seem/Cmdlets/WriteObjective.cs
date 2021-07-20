using Osu.Cof.Ferm.Heuristics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommunications.Write, "Objective")]
    public class WriteObjective : WriteHeuristicResultsCmdlet
    {
        [Parameter(HelpMessage = "Number of iterations between CSV file lines. Default is 1, which logs every objective function value.")]
        [ValidateRange(1, Int32.MaxValue)]
        public int Step;

        public WriteObjective()
        {
            this.LimitGB = 3.0F; // approximate upper bound of what fread() can load in R
            this.Step = 1;
        }

        protected override void ProcessRecord()
        {
            if (this.Results!.PositionsEvaluated.Count < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.Results));
            }

            using StreamWriter writer = this.GetWriter();

            // for now, perform no reduction when Object.ReferenceEquals(lowSolution, highSolution) is true
            if (this.ShouldWriteHeader())
            {
                HeuristicResultPosition position = this.Results.PositionsEvaluated[0];
                HeuristicSolutionPool solution = this.Results[position].Pool;
                if ((solution.High == null) || (solution.Low == null))
                {
                    throw new NotSupportedException("Cannot generate header because first result is missing a high solution or a low solution.");
                }
                StringBuilder line = new(WriteCmdlet.GetHeuristicAndPositionCsvHeader(this.Results) + ",iteration,count");

                string lowMoveLogHeader = "lowMoveLog";
                IHeuristicMoveLog? lowMoveLog = solution.Low!.GetMoveLog();
                if (lowMoveLog != null)
                {
                    lowMoveLogHeader = lowMoveLog.GetCsvHeader("low");
                }
                line.Append("," + lowMoveLogHeader);

                line.Append(",low,lowCandidate,min,percentile2.5,percentile5,lowerQuartile,median,mean,upperQuartile,percentile95,percentile97.5,max");

                string highMoveLogHeader = "highMoveLog";
                IHeuristicMoveLog? highMoveLog = solution.High!.GetMoveLog();
                if (highMoveLog != null)
                {
                    highMoveLogHeader = highMoveLog.GetCsvHeader("high");
                }
                line.Append("," + highMoveLogHeader);

                line.Append(",high,highCandidate");
                writer.WriteLine(line);
            }

            // sort each discount rate's runs by decreasing objective function value  
            List<List<(float, HeuristicResultPosition)>> solutionsByFinancialIndexAndValue = new();
            for (int positionIndex = 0; positionIndex < this.Results.PositionsEvaluated.Count; ++positionIndex)
            {
                HeuristicResultPosition position = this.Results.PositionsEvaluated[positionIndex];
                HeuristicResult result = this.Results[position];
                HeuristicObjectiveDistribution distribution = result.Distribution;
                int maxFinancialIndex = position.FinancialIndex;
                while (maxFinancialIndex >= solutionsByFinancialIndexAndValue.Count)
                {
                    solutionsByFinancialIndexAndValue.Add(new());
                }

                Heuristic? highHeuristic = result.Pool.High;
                if (highHeuristic == null)
                {
                    throw new NotSupportedException("Result at position " + positionIndex + " is missing a high solution.");
                }

                List<(float, HeuristicResultPosition)> runsForDiscountRate = solutionsByFinancialIndexAndValue[maxFinancialIndex];
                runsForDiscountRate.Add((highHeuristic.FinancialValue.GetHighestValueWithDefaulting(position.RotationIndex, maxFinancialIndex), position));
            }
            for (int financialIndex = 0; financialIndex < solutionsByFinancialIndexAndValue.Count; ++financialIndex)
            {
                List<(float Objective, HeuristicResultPosition)> runsForDiscountRate = solutionsByFinancialIndexAndValue[financialIndex];
                runsForDiscountRate.Sort((run1, run2) => run2.Objective.CompareTo(run1.Objective)); // sort descending
            }

            // list runs in declining order of objective function value for logging
            // Runs are listed in groups with each discount rate having the ability to be represented within each group. This controls for reduction in
            // land expectation values with increasing discount rate, enabling preferentiall logging of the move histories for the most desirable runs.
            List<HeuristicResultPosition> prioritizedResults = new();
            int[] resultIndicesByFinancialScenario = new int[solutionsByFinancialIndexAndValue.Count];
            for (bool atLeastOneRunAddedByInnerLoop = true; atLeastOneRunAddedByInnerLoop; )
            {
                atLeastOneRunAddedByInnerLoop = false;
                for (int financialIndex = 0; financialIndex < solutionsByFinancialIndexAndValue.Count; ++financialIndex)
                {
                    List<(float Objective, HeuristicResultPosition Position)> runsForScenario = solutionsByFinancialIndexAndValue[financialIndex];
                    int resultIndexForScenario = resultIndicesByFinancialScenario[financialIndex];
                    if (resultIndexForScenario < runsForScenario.Count)
                    {
                        prioritizedResults.Add(runsForScenario[resultIndexForScenario].Position);
                        resultIndicesByFinancialScenario[financialIndex] = ++resultIndexForScenario;
                        atLeastOneRunAddedByInnerLoop = true;
                    }
                }
            }

            // log runs in declining priority order until either all runs are logged or the file size limit is reached
            long maxFileSizeInBytes = this.GetMaxFileSizeInBytes();
            for (int positionIndex = 0; positionIndex < prioritizedResults.Count; ++positionIndex)
            {
                HeuristicResultPosition position = prioritizedResults[positionIndex];
                HeuristicResult result = this.Results[position];
                string heuristicAndPosition = WriteCmdlet.GetHeuristicAndPositionCsvValues(result.Pool, this.Results, position);
                Heuristic highHeuristic = result.Pool.High!;
                Heuristic lowHeuristic = result.Pool.Low!;

                // solution distribution is informative and should be logged in this case
                // for now, assume high and low solutions use the same parameters
                IList<float> acceptedFinancialValueByMoveHigh = highHeuristic.FinancialValue.GetAcceptedValuesWithDefaulting(position);
                IList<float> acceptedFinancialValueByMoveLow = lowHeuristic.FinancialValue.GetAcceptedValuesWithDefaulting(position);
                IList<float> candidateFinancialValueByMoveHigh = highHeuristic.FinancialValue.GetCandidateValuesWithDefaulting(position);
                IList<float> candidateFinancialValueByMoveLow = lowHeuristic.FinancialValue.GetCandidateValuesWithDefaulting(position);

                IHeuristicMoveLog? highMoveLog = highHeuristic.GetMoveLog();
                IHeuristicMoveLog? lowMoveLog = lowHeuristic.GetMoveLog();
                int maximumMoves = result.Distribution.GetMaximumMoves();
                Debug.Assert((maximumMoves >= acceptedFinancialValueByMoveLow.Count) && (maximumMoves >= candidateFinancialValueByMoveLow.Count));
                Debug.Assert((maximumMoves >= acceptedFinancialValueByMoveHigh.Count) && (maximumMoves >= candidateFinancialValueByMoveHigh.Count));
                for (int moveIndex = 0; moveIndex < maximumMoves; moveIndex += this.Step)
                {
                    string? lowMove = null;
                    if ((lowMoveLog != null) && (lowMoveLog.LengthInMoves > moveIndex))
                    {
                        lowMove = lowMoveLog.GetCsvValues(position, moveIndex);
                    }
                    string? lowObjectiveFunction = null;
                    if (acceptedFinancialValueByMoveLow.Count > moveIndex)
                    {
                        lowObjectiveFunction = acceptedFinancialValueByMoveLow[moveIndex].ToString(CultureInfo.InvariantCulture);
                    }
                    string? lowObjectiveFunctionForMove = null;
                    if (candidateFinancialValueByMoveLow.Count > moveIndex)
                    {
                        lowObjectiveFunctionForMove = candidateFinancialValueByMoveLow[moveIndex].ToString(CultureInfo.InvariantCulture);
                    }

                    DistributionStatistics moveStatistics = result.Distribution.GetFinancialStatisticsForMove(moveIndex);
                    string runsWithMoveAtIndex = moveStatistics.Count.ToString(CultureInfo.InvariantCulture);
                    string minObjectiveFunction = moveStatistics.Minimum.ToString(CultureInfo.InvariantCulture);
                    string? lowerQuartileObjectiveFunction = null;
                    if (moveStatistics.LowerQuartile != null)
                    {
                        lowerQuartileObjectiveFunction = moveStatistics.LowerQuartile.Value.ToString(CultureInfo.InvariantCulture);
                    }
                    string? twoPointFivePercentileObjectiveFunction = null;
                    if (moveStatistics.TwoPointFivePercentile != null)
                    {
                        twoPointFivePercentileObjectiveFunction = moveStatistics.TwoPointFivePercentile.Value.ToString(CultureInfo.InvariantCulture);
                    }
                    string? fifthPercentileObjectiveFunction = null;
                    if (moveStatistics.FifthPercentile != null)
                    {
                        fifthPercentileObjectiveFunction = moveStatistics.FifthPercentile.Value.ToString(CultureInfo.InvariantCulture);
                    }

                    string medianObjectiveFunction = moveStatistics.Median.ToString(CultureInfo.InvariantCulture);
                    string meanObjectiveFunction = moveStatistics.Mean.ToString(CultureInfo.InvariantCulture);
                    string? upperQuartileObjectiveFunction = null;
                    if (moveStatistics.UpperQuartile != null)
                    {
                        upperQuartileObjectiveFunction = moveStatistics.UpperQuartile.Value.ToString(CultureInfo.InvariantCulture);
                    }
                    string? ninetyFifthPercentileObjectiveFunction = null;
                    if (moveStatistics.NinetyFifthPercentile != null)
                    {
                        ninetyFifthPercentileObjectiveFunction = moveStatistics.NinetyFifthPercentile.Value.ToString(CultureInfo.InvariantCulture);
                    }
                    string? ninetySevenPointFivePercentileObjectiveFunction = null;
                    if (moveStatistics.NinetySevenPointFivePercentile != null)
                    {
                        ninetySevenPointFivePercentileObjectiveFunction = moveStatistics.NinetySevenPointFivePercentile.Value.ToString(CultureInfo.InvariantCulture);
                    }
                    string maxObjectiveFunction = moveStatistics.Maximum.ToString(CultureInfo.InvariantCulture);

                    string? highMove = null;
                    if ((highMoveLog != null) && (highMoveLog.LengthInMoves > moveIndex))
                    {
                        highMove = highMoveLog.GetCsvValues(position, moveIndex);
                    }
                    string highObjectiveFunction = String.Empty;
                    if (acceptedFinancialValueByMoveHigh.Count > moveIndex)
                    {
                        highObjectiveFunction = acceptedFinancialValueByMoveHigh[moveIndex].ToString(CultureInfo.InvariantCulture);
                    }
                    string? highObjectiveFunctionForMove = null;
                    if (candidateFinancialValueByMoveHigh.Count > moveIndex)
                    {
                        highObjectiveFunctionForMove = candidateFinancialValueByMoveHigh[moveIndex].ToString(CultureInfo.InvariantCulture);
                    }

                    Debug.Assert((acceptedFinancialValueByMoveLow.Count <= moveIndex) || (moveStatistics.Maximum >= acceptedFinancialValueByMoveLow[moveIndex]));
                    Debug.Assert((acceptedFinancialValueByMoveHigh.Count <= moveIndex) || (moveStatistics.Minimum <= acceptedFinancialValueByMoveHigh[moveIndex]));
                    writer.WriteLine(heuristicAndPosition + "," +
                                     moveIndex + "," +
                                     runsWithMoveAtIndex + "," +
                                     lowMove + "," +
                                     lowObjectiveFunction + "," +
                                     lowObjectiveFunctionForMove + "," +
                                     minObjectiveFunction + "," +
                                     twoPointFivePercentileObjectiveFunction + "," +
                                     fifthPercentileObjectiveFunction + "," +
                                     lowerQuartileObjectiveFunction + "," +
                                     medianObjectiveFunction + "," +
                                     meanObjectiveFunction + "," +
                                     upperQuartileObjectiveFunction + "," +
                                     ninetyFifthPercentileObjectiveFunction + "," +
                                     ninetySevenPointFivePercentileObjectiveFunction + "," +
                                     maxObjectiveFunction + "," +
                                     highMove + "," +
                                     highObjectiveFunction + "," +
                                     highObjectiveFunctionForMove);
                }

                if (writer.BaseStream.Length > maxFileSizeInBytes)
                {
                    this.WriteWarning("Write-Objective: File size limit of " + this.LimitGB.ToString("0.00") + " GB exceeded.");
                    break;
                }
            }
        }
    }
}
