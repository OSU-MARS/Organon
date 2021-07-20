using Osu.Cof.Ferm.Extensions;
using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using Osu.Cof.Ferm.Tree;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommunications.Write, "SnagsAndLogs")]
    public class WriteSnagsAndLogs : WriteStandTrajectory
    {
        protected override void ProcessRecord()
        {
            this.ValidateParameters();

            using StreamWriter writer = this.GetWriter();

            // header
            bool resultsSpecified = this.Results != null;
            if (this.ShouldWriteHeader())
            {
                HeuristicParameters? heuristicParameters = null;
                if (resultsSpecified)
                {
                    heuristicParameters = WriteCmdlet.GetFirstHeuristicParameters(this.Results);
                }
                else if (this.Trajectories![0].Heuristic != null)
                {
                    heuristicParameters = this.Trajectories[0].Heuristic!.GetParameters();
                }

                writer.WriteLine(WriteCmdlet.GetHeuristicAndPositionCsvHeader(heuristicParameters) + ",standAge,species,diameter class,snags,logs");
            }

            // rows for periods
            long maxFileSizeInBytes = this.GetMaxFileSizeInBytes();
            int maxIndex = resultsSpecified ? this.Results!.PositionsEvaluated.Count : this.Trajectories!.Count;
            for (int positionOrTrajectoryIndex = 0; positionOrTrajectoryIndex < maxIndex; ++positionOrTrajectoryIndex)
            {
                OrganonStandTrajectory highTrajectory = this.GetHighestTrajectoryAndLinePrefix(positionOrTrajectoryIndex, out StringBuilder linePrefix, out int _, out int _);

                SnagLogTable snagsAndLogs = new(highTrajectory, this.MaximumDiameter, this.DiameterClassSize);
                for (int periodIndex = 0; periodIndex < highTrajectory.PlanningPeriods; ++periodIndex)
                {
                    OrganonStand? stand = highTrajectory.StandByPeriod[periodIndex];
                    Debug.Assert(stand != null);
                    string standAge = stand.AgeInYears.ToString(CultureInfo.InvariantCulture);

                    foreach (KeyValuePair<FiaCode, float[,]> speciesAndSnags in snagsAndLogs.SnagsPerHectareBySpeciesAndDiameterClass)
                    {
                        FiaCode species = speciesAndSnags.Key;
                        float[,] logs = snagsAndLogs.LogsPerHectareBySpeciesAndDiameterClass[species];
                        float[,] snags = speciesAndSnags.Value;
                        string standAgeAndSpeciesCode = standAge + "," + species.ToFourLetterCode();
                        for (int diameterClassIndex = 0; diameterClassIndex < snagsAndLogs.DiameterClasses; ++diameterClassIndex)
                        {
                            string diameter = snagsAndLogs.GetDiameter(diameterClassIndex).ToString("0.0", CultureInfo.InvariantCulture);
                            string snagsPerHectare = snags[periodIndex, diameterClassIndex].ToString("0.00", CultureInfo.InvariantCulture);
                            string logsPerHectare = logs[periodIndex, diameterClassIndex].ToString("0.00", CultureInfo.InvariantCulture);

                            writer.WriteLine(linePrefix + "," +
                                             standAgeAndSpeciesCode + "," +
                                             diameter + "," +
                                             snagsPerHectare + "," +
                                             logsPerHectare);
                        }
                    }
                }

                if (writer.BaseStream.Length > maxFileSizeInBytes)
                {
                    this.WriteWarning("Write-SnagsAndLogs: File size limit of " + this.LimitGB.ToString("0.00") + " GB exceeded.");
                    break;
                }
            }
        }
    }
}
