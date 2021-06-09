﻿using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using System;
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
            bool runsSpecified = this.Results != null;
            if (this.ShouldWriteHeader())
            {
                StringBuilder line = new("stand,heuristic");

                HeuristicParameters? heuristicParametersForHeader = null;
                if (runsSpecified)
                {
                    heuristicParametersForHeader = WriteCmdlet.GetFirstHeuristicParameters(this.Results);
                }
                else if (this.Trajectories![0].Heuristic != null)
                {
                    heuristicParametersForHeader = this.Trajectories[0].Heuristic!.GetParameters();
                }

                if (heuristicParametersForHeader != null)
                {
                    string heuristicParameters = heuristicParametersForHeader.GetCsvHeader();
                    if (String.IsNullOrEmpty(heuristicParameters) == false)
                    {
                        // TODO: if needed, check if heuristics have different parameters
                        line.Append("," + heuristicParameters);
                    }
                }

                line.Append("," + WriteCmdlet.RateAndAgeCsvHeader + ",standAge,species,diameter class,snags,logs");
                writer.WriteLine(line);
            }

            // rows for periods
            long maxFileSizeInBytes = this.GetMaxFileSizeInBytes();
            int maxIndex = runsSpecified ? this.Results!.Distributions.Count : this.Trajectories!.Count;
            for (int runOrTrajectoryIndex = 0; runOrTrajectoryIndex < maxIndex; ++runOrTrajectoryIndex)
            {
                OrganonStandTrajectory highestTrajectory = this.GetHighestTrajectoryAndLinePrefix(runOrTrajectoryIndex, out StringBuilder linePrefix, out float _);

                SnagLogTable snagsAndLogs = new(highestTrajectory, this.MaximumDiameter, this.DiameterClassSize);
                for (int periodIndex = 0; periodIndex < highestTrajectory.PlanningPeriods; ++periodIndex)
                {
                    OrganonStand? stand = highestTrajectory.StandByPeriod[periodIndex];
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
