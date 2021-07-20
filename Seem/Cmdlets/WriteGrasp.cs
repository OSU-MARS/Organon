using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Organon;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommunications.Write, "Grasp")]
    public class WriteGrasp : WriteCmdlet
    {
        [Parameter]
        [ValidateNotNull]
        public HeuristicResults? Results { get; set; }

        protected override void ProcessRecord()
        {
            Debug.Assert(this.Results != null);

            using StreamWriter writer = this.GetWriter();

            // header
            if (this.ShouldWriteHeader())
            {
                writer.WriteLine("stand,heuristic,alpha,selected,rejected");
            }

            // rows for bin counts
            HeuristicResultPosition position = this.Results.PositionsEvaluated[0];
            HeuristicSolutionPool solutions = this.Results[position].Pool;
            if (solutions.High == null)
            {
                throw new NotSupportedException("First solution pool is missing a high solution");
            }
            OrganonStandTrajectory highTrajectory = solutions.High.GetBestTrajectoryWithDefaulting(position);

            string linePrefix = highTrajectory.Name + "," + solutions.High.GetName();
            long maxFileSizeInBytes = this.GetMaxFileSizeInBytes();
            for (int binIndex = 0; binIndex < this.Results.GraspReactivity.SelectionHistogram.Length; ++binIndex)
            {
                string binCenter = (this.Results.GraspReactivity.GreedinessBinWidth * (binIndex + 0.5F)).ToString(CultureInfo.InvariantCulture);
                string selected = this.Results.GraspReactivity.SelectionHistogram[binIndex].ToString(CultureInfo.InvariantCulture);
                string rejected = this.Results.GraspReactivity.RejectionHistogram[binIndex].ToString(CultureInfo.InvariantCulture);
                writer.WriteLine(linePrefix + "," +
                                 binCenter + "," +
                                 selected + "," +
                                 rejected);
            }

            if (writer.BaseStream.Length > maxFileSizeInBytes)
            {
                this.WriteWarning("Write-SnagsAndLogs: File size limit of " + this.LimitGB.ToString("0.00") + " GB exceeded.");
            }
        }
    }
}
