using System.Globalization;

namespace Osu.Cof.Ferm.Heuristics
{
    // if needed, derive parameter classes specific to ascent and enumeration
    public class PrescriptionParameters : HeuristicParameters
    {
        // for now, intensity step is relative to the density of the stand before thinning
        // For example if the step size is 5% and the first thin removes 50%, then the second thin will be evaluated in 10% steps. This is
        // potentially a computationally sensitive parameter as prescription enumeration cost can increase at high powers. The step size
        // individually affects the cost of enumerating from above, from below, and proportionally, resulting in a quadratic increase for each
        // thin if all three modes are enabled. If three thins are also configured, enumeration cost therefore increases as the ninth power of
        // the step size. In this case, halving the step size increases compute time by a factor of 512. If the full step size takes one hour
        // to run the half step will take 21 days.
        public float DefaultIntensityStepSize { get; set; } // ascent and enumeration

        public float FromAbovePercentageUpperLimit { get; set; } // ascent and enumeration
        public float FromBelowPercentageUpperLimit { get; set; } // ascent and enumeration
        public float ProportionalPercentageUpperLimit { get; set; } // ascent and enumeration

        public bool LogAllMoves { get; set; } // ascent and enumeration

        public float MaximumIntensity { get; set; } // ascent and enumeration
        public float MaximumIntensityStepSize { get; set; } // enumeration
        public float MinimumIntensity { get; set; } // ascent and enumeration
        public float MinimumIntensityStepSize { get; set; } // ascent
        public float StepSizeMultiplier { get; set; }

        public PrescriptionUnits Units { get; set; } // ascent and enumeration

        public PrescriptionParameters()
        {
            this.MinimumConstructionGreediness = Constant.Grasp.FullyGreedyConstructionForMaximization;
            this.InitialThinningProbability = 0.0F;
            this.LogAllMoves = false;

            this.DefaultIntensityStepSize = Constant.PrescriptionSearchDefault.DefaultIntensityStepSize;

            this.FromAbovePercentageUpperLimit = 100.0F;
            this.ProportionalPercentageUpperLimit = 100.0F;
            this.FromBelowPercentageUpperLimit = 100.0F;

            this.MaximumIntensity = Constant.PrescriptionSearchDefault.MaximumIntensity;
            this.MaximumIntensityStepSize = Constant.PrescriptionSearchDefault.MaximumIntensityStepSize;
            this.MinimumIntensity = Constant.PrescriptionSearchDefault.MinimumIntensity;
            this.MinimumIntensityStepSize = Constant.PrescriptionSearchDefault.MinimumIntensityStepSize;
            this.StepSizeMultiplier = Constant.PrescriptionSearchDefault.StepSizeMultiplier;

            this.Units = Constant.PrescriptionSearchDefault.Units;
        }

        public override string GetCsvHeader()
        {
            return base.GetCsvHeader() + ",units,maxAbove,maxProportional,maxBelow,minIntensity,minStep,stepMultiplier,defaultStep,maxStep,maxIntensity";
        }

        public override string GetCsvValues()
        {
            return base.GetCsvValues() + "," +
                   this.Units.ToString() + "," +
                   this.FromAbovePercentageUpperLimit.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                   this.ProportionalPercentageUpperLimit.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                   this.FromBelowPercentageUpperLimit.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                   this.MinimumIntensity.ToString(CultureInfo.InvariantCulture) + "," +
                   this.MinimumIntensityStepSize.ToString(CultureInfo.InvariantCulture) + "," +
                   this.StepSizeMultiplier.ToString(CultureInfo.InvariantCulture) + "," +
                   this.DefaultIntensityStepSize.ToString(CultureInfo.InvariantCulture) + "," +
                   this.MaximumIntensityStepSize.ToString(CultureInfo.InvariantCulture) + "," +
                   this.MaximumIntensity.ToString(CultureInfo.InvariantCulture);
        }
    }
}
