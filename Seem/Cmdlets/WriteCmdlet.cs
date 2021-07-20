using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    public class WriteCmdlet : Cmdlet
    {
        private bool openedExistingFile;

        [Parameter]
        public SwitchParameter Append;

        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public string? CsvFile;

        [Parameter(HelpMessage = "Maximum size of output file in gigabytes.")]
        [ValidateRange(0.1F, 100.0F)]
        public float LimitGB { get; set; }

        public WriteCmdlet()
        {
            this.Append = false;
            this.LimitGB = 1.0F; // sanity default, may be set higher in derived classes
            this.openedExistingFile = false;
        }

        protected static HeuristicParameters GetFirstHeuristicParameters(HeuristicResults? results)
        {
            Debug.Assert(results != null);

            Heuristic? highHeuristic = results[results.PositionsEvaluated[0]].Pool.High;
            if (highHeuristic == null)
            {
                throw new NotSupportedException("Can't obtain heuristic parameters.");
            }
            return highHeuristic.GetParameters();
        }

        protected static string GetHeuristicAndPositionCsvHeader(HeuristicParameters? heuristicParameters)
        {
            string? parameterHeader = null;
            if (heuristicParameters != null)
            {
                parameterHeader = heuristicParameters.GetCsvHeader() + ",";
            }
            return "stand,heuristic," + parameterHeader + "thin1,thin2,thin3,rotation,financialScenario";
        }

        protected static string GetHeuristicAndPositionCsvHeader(HeuristicResults? results)
        {
            HeuristicParameters? heuristicParameters = null;
            if (results != null)
            {
                heuristicParameters = WriteCmdlet.GetFirstHeuristicParameters(results);
            }
            return WriteCmdlet.GetHeuristicAndPositionCsvHeader(heuristicParameters);
        }

        protected static string GetHeuristicAndPositionCsvValues(HeuristicSolutionPool solutions, HeuristicResults results, HeuristicResultPosition position)
        {
            if ((solutions.High == null) || (solutions.Low == null))
            {
                throw new NotSupportedException("Solution pool is missing a high or low solution.");
            }

            int firstThinPeriod = results.FirstThinPeriods[position.FirstThinPeriodIndex];
            int secondThinPeriod = results.SecondThinPeriods[position.SecondThinPeriodIndex];
            int thirdThinPeriod = results.ThirdThinPeriods[position.ThirdThinPeriodIndex];
            int endOfRotationPeriod = results.RotationLengths[position.RotationIndex];
            OrganonStandTrajectory highTrajectory = solutions.High.GetBestTrajectoryWithDefaulting(position);

            string? heuristicParameters = solutions.High.GetParameters().GetCsvValues();
            string? firstThinAge = firstThinPeriod != Constant.NoThinPeriod ? highTrajectory.GetStartOfPeriodAge(firstThinPeriod).ToString(CultureInfo.InvariantCulture) : null;
            string? secondThinAge = secondThinPeriod != Constant.NoThinPeriod ? highTrajectory.GetStartOfPeriodAge(secondThinPeriod).ToString(CultureInfo.InvariantCulture) : null;
            string? thirdThinAge = thirdThinPeriod != Constant.NoThinPeriod ? highTrajectory.GetStartOfPeriodAge(thirdThinPeriod).ToString(CultureInfo.InvariantCulture) : null;
            string rotationLength = highTrajectory.GetEndOfPeriodAge(endOfRotationPeriod).ToString(CultureInfo.InvariantCulture);
            string financialScenario = results.FinancialScenarios.Name[position.FinancialIndex];
            
            return highTrajectory.Name + "," + 
                   solutions.High.GetName() + "," + 
                   heuristicParameters + "," + 
                   firstThinAge + "," + 
                   secondThinAge + "," + 
                   thirdThinAge + "," + 
                   rotationLength + "," + 
                   financialScenario;
        }

        protected long GetMaxFileSizeInBytes()
        {
            return (long)(1E9F * this.LimitGB);
        }

        protected StreamWriter GetWriter()
        {
            FileMode fileMode = this.Append ? FileMode.Append : FileMode.Create;
            if (fileMode == FileMode.Append)
            {
                this.openedExistingFile = File.Exists(this.CsvFile);
            }
            return new StreamWriter(new FileStream(this.CsvFile!, fileMode, FileAccess.Write, FileShare.Read));
        }

        protected static void GetBasalAreaConversion(Units inputUnits, Units outputUnits, out float basalAreaConversionFactor)
        {
            if (inputUnits == outputUnits)
            {
                basalAreaConversionFactor = 1.0F;
                return;
            }

            // English to metric
            if ((inputUnits == Units.English) && (outputUnits == Units.Metric))
            {
                basalAreaConversionFactor = Constant.AcresPerHectare * Constant.MetersPerFoot * Constant.MetersPerFoot;
                return;
            }

            throw new NotSupportedException();
        }

        protected static void GetDimensionConversions(Units inputUnits, Units outputUnits, out float areaConversionFactor, out float dbhConversionFactor, out float heightConversionFactor)
        {
            if (inputUnits == outputUnits)
            {
                areaConversionFactor = 1.0F;
                dbhConversionFactor = 1.0F;
                heightConversionFactor = 1.0F;
                return;
            }

            // English to metric
            if ((inputUnits == Units.English) && (outputUnits == Units.Metric))
            {
                areaConversionFactor = Constant.AcresPerHectare;
                dbhConversionFactor = Constant.CentimetersPerInch;
                heightConversionFactor = Constant.MetersPerFoot;
                return;
            }

            throw new NotSupportedException();
        }

        protected bool ShouldWriteHeader()
        {
            return this.openedExistingFile == false;
        }
    }
}
