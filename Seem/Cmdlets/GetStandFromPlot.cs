using Osu.Cof.Ferm.Organon;
using Osu.Cof.Ferm.Data;
using Osu.Cof.Ferm.Tree;
using System;
using System.Management.Automation;
using System.Collections.Generic;

namespace Osu.Cof.Ferm.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "StandFromPlot")]
    public class GetStandFromPlot : Cmdlet
    {
        [Parameter(Mandatory = true)]
        [ValidateRange(0, 1000)]
        public int Age { get; set; }

        [Parameter]
        [ValidateRange(0.1, Constant.Maximum.ExpansionFactor)]
        public float? ExpansionFactor { get; set; }

        [Parameter]
        public TreeModel Model { get; set; }

        [Parameter]
        [ValidateRange(1.0F, Constant.Maximum.PlantingDensityInTreesPerHectare)]
        public float? PlantingDensity { get; set; }

        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public List<int>? Plots { get; set; }

        [Parameter]
        [ValidateRange(1.0F, Constant.Maximum.SiteIndexInFeet)]
        public float SiteIndex { get; set; }

        [Parameter]
        [ValidateRange(1, Int32.MaxValue)]
        public int? Trees { get; set; }

        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public string? Xlsx { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        public string XlsxSheet { get; set; }

        public GetStandFromPlot()
        {
            this.ExpansionFactor = null;
            this.Model = TreeModel.OrganonNwo;
            this.SiteIndex = 130.0F;
            this.Trees = null;
            this.Xlsx = null;
            this.XlsxSheet = "1";
        }

        protected override void ProcessRecord()
        {
            PlotsWithHeight plot;
            if (this.ExpansionFactor.HasValue)
            {
                plot = new PlotsWithHeight(this.Plots!, this.ExpansionFactor.Value);
            }
            else
            {
                plot = new PlotsWithHeight(this.Plots!);
            }
            plot.Read(this.Xlsx!, this.XlsxSheet);

            OrganonConfiguration configuration = new(OrganonVariant.Create(this.Model));
            OrganonStand stand;
            if (this.Trees.HasValue)
            {
                stand = plot.ToOrganonStand(configuration, this.Age, this.SiteIndex, this.Trees.Value);
            }
            else
            {
                stand = plot.ToOrganonStand(configuration, this.Age, this.SiteIndex);
            }
            if (this.PlantingDensity.HasValue)
            {
                stand.PlantingDensityInTreesPerHectare = this.PlantingDensity.Value;
            }
            this.WriteObject(stand);
        }
    }
}
