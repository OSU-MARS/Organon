using Osu.Cof.Ferm.Organon;
using Osu.Cof.Ferm.Tree;
using System.Management.Automation;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "StandTrajectory")]
    public class GetStandTrajectory : Cmdlet
    {
        [Parameter]
        [ValidateNotNullOrEmpty]
        public string? Name { get; set; }

        [Parameter]
        [ValidateRange(1, 100)]
        public int PlanningPeriods { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 100.0F)]
        public float ProportionalThinPercentage { get; set; }

        [Parameter(Mandatory = true)]
        [ValidateNotNull]
        public OrganonStand? Stand { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 100.0F)]
        public float ThinFromAbovePercentage { get; set; }

        [Parameter]
        [ValidateRange(0.0F, 100.0F)]
        public float ThinFromBelowPercentage { get; set; }

        [Parameter]
        [ValidateRange(1, 100)]
        public int ThinPeriod { get; set; }

        [Parameter]
        [ValidateNotNull]
        public TreeVolume TreeVolume { get; set; }

        public GetStandTrajectory()
        {
            this.Name = null;
            this.PlanningPeriods = 20; // 100 years of simulation with Organon's 5 year timestep
            this.ProportionalThinPercentage = 0.0F; // %
            this.ThinFromAbovePercentage = 0.0F; // %
            this.ThinFromBelowPercentage = 0.0F; // %
            this.ThinPeriod = Constant.NoThinPeriod; // no stand entry
            this.TreeVolume = TreeVolume.Default;
        }

        protected override void ProcessRecord()
        {
            if (this.ThinPeriod >= this.PlanningPeriods)
            {
                throw new ParameterOutOfRangeException(nameof(this.ThinPeriod));
            }

            OrganonConfiguration configuration = new(new OrganonVariantNwo());
            OrganonStandTrajectory trajectory = new(this.Stand!, configuration, this.TreeVolume, this.PlanningPeriods);
            if (this.Name != null)
            {
                trajectory.Name = this.Name;
            }
            if (this.ThinPeriod != Constant.NoThinPeriod)
            {
                trajectory.Treatments.Harvests.Add(new ThinByPrescription(this.ThinPeriod)
                {
                    FromAbovePercentage = this.ThinFromAbovePercentage,
                    ProportionalPercentage = this.ProportionalThinPercentage,
                    FromBelowPercentage = this.ThinFromBelowPercentage
                });
            }

            // performance: if needed, remove unnecessary repetition of stand simulation for each discount rate
            // Only one growth simulation is necessary but StandTrajectory lacks an API to recompute its net present value arrays
            // for a new discount rate.
            trajectory.Simulate();

            this.WriteObject(trajectory);
        }
    }
}
