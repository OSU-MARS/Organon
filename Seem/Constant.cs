using Osu.Cof.Ferm.Heuristics;
using Osu.Cof.Ferm.Tree;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Osu.Cof.Ferm
{
    internal static class Constant
    {
        public const float AcresPerHectare = 2.47105F;
        public const int AllFinancialScenariosPosition = -2;
        public const int AllRotationPosition = -2;
        public const float CentimetersPerInch = 2.54000F;
        // 100 * pi / (4 * 43560), from definition of crown competition factor
        public const float CrownCompetionConstantEnglish = 0.001803026F;
        public const float CubicFeetPerCubicMeter = 35.3147F;
        public const float CubicMetersPerCubicFoot = 0.0283168F;
        public const float DbhHeight = 1.37F; // cm
        public const string DefaultPercentageFormat = "0.0#";
        public const int DefaultPlanningPeriods = 9;
        public const string DefaultProbabilityFormat = "0.00##";
        public const int DefaultSolutionPoolSize = 4;
        public const int DefaultThinningPeriod = 3;
        public const int DefaultTimeStepInYears = 5;
        public const float FeetPerMeter = 3.28084F;
        public const float ForestersEnglish = 0.005454154F;
        public const float HectaresPerAcre = 0.404685F;
        public const float InchesPerCentimeter = 0.393701F;
        public const int MaximizeForAllPlanningPeriods = -2;
        public const float MetersPerFoot = 0.3048F;
        public const float NaturalLogOf10 = 2.3025850930F;
        public const int NoHarvestPeriod = 0;
        public const int NoThinPeriod = -1;
        // number of height strata must be an exact multiple of SIMD width: multiples of 4 for VEX 128, 8 for VEX 256
        public const int OrganonHeightStrata = 40;
        public const float Pi = 3.14159265F;
        public const float PolymorphicLocusThreshold = 0.95F;
        public const float RedAlderAdditionalMortalityGrowthEffectiveAgeInYears = 55.0F;
        public const float ReinekeExponent = 1.605F;
        // 0.00003 and smaller result in expected ArgumentOutOfRangeExceptions due to single precision
        // However, 0.0001 still results in rare exceptions. The underlying cause is unclear.
        public const float RoundTowardsZeroTolerance = 0.001F;

        public static readonly ReadOnlyCollection<FiaCode> NwoSmcSpecies = new(new List<FiaCode>()
        {
            FiaCode.PseudotsugaMenziesii,
            FiaCode.AbiesGrandis,
            FiaCode.TsugaHeterophylla,
            FiaCode.ThujaPlicata,
            FiaCode.TaxusBrevifolia,
            FiaCode.ArbutusMenziesii,
            FiaCode.AcerMacrophyllum,
            FiaCode.QuercusGarryana,
            FiaCode.AlnusRubra,
            FiaCode.CornusNuttallii,
            FiaCode.Salix
        });
        public static readonly ReadOnlyCollection<FiaCode> RapSpecies = new(new List<FiaCode>()
        {
            FiaCode.AlnusRubra,
            FiaCode.PseudotsugaMenziesii,
            FiaCode.TsugaHeterophylla,
            FiaCode.ThujaPlicata,
            FiaCode.AcerMacrophyllum,
            FiaCode.CornusNuttallii,
            FiaCode.Salix
        });
        public static readonly ReadOnlyCollection<FiaCode> SwoSpecies = new(new List<FiaCode>()
        {
            FiaCode.PseudotsugaMenziesii,
            FiaCode.AbiesConcolor,
            FiaCode.AbiesGrandis,
            FiaCode.PinusPonderosa,
            FiaCode.PinusLambertiana,
            FiaCode.CalocedrusDecurrens,
            FiaCode.TsugaHeterophylla,
            FiaCode.ThujaPlicata,
            FiaCode.TaxusBrevifolia,
            FiaCode.ArbutusMenziesii,
            FiaCode.ChrysolepisChrysophyllaVarChrysophylla,
            FiaCode.NotholithocarpusDensiflorus,
            FiaCode.QuercusChrysolepis,
            FiaCode.AcerMacrophyllum,
            FiaCode.QuercusGarryana,
            FiaCode.QuercusKelloggii,
            FiaCode.AlnusRubra,
            FiaCode.CornusNuttallii,
            FiaCode.Salix
        });

        public static class Bucking
        {
            public const float DefaultMaximumDiameterInCentimeters = 120.0F;
            public const float DefaultMaximumHeightInMeters = 75.0F;
            public const float DefectAndBreakageReduction = 0.955F; // 100 - 4.5%
            public const float DiameterClassSizeInCentimeters = 1.0F;
            public const float EvaluationHeightStep = 0.5F; // m
            public const float HeightClassSizeInMeters = 1.0F; // m
            public const float KerfProcessingHead = 0.007F; // m
            public const float LogLengthRegenerationHarvest = Constant.MetersPerFoot * 40.0F; // m
            public const float LogLengthThinning = Constant.MetersPerFoot * 24.0F; // m
            public const float LogTaperSegmentationLength = Constant.MetersPerFoot * 8.0F; // m
            public const float MinimumBasalArea4SawEnglish = 0.14F; // 5 inch DBH + a bit for bark
            public const float MinimumLogLength2Saw = Constant.MetersPerFoot * 12.0F; // m
            public const float MinimumLogLength3Saw = Constant.MetersPerFoot * 12.0F; // m
            public const float MinimumLogLength4Saw = Constant.MetersPerFoot * 8.0F; // m, typically specified as 12 but often 8 feet in practice
            public const float MinimumScalingDiameter2Saw = Constant.CentimetersPerInch * 12.0F; // cm
            public const float MinimumScalingDiameter3Saw = Constant.CentimetersPerInch * 6.0F; // cm
            public const float MinimumScalingDiameter4Saw = Constant.CentimetersPerInch * 5.0F; // cm
            public const float MinimumLogScribner2Saw = 60; // board feet
            public const float MinimumLogScribner3Saw = 50; // board feet
            public const float MinimumLogScribner4Saw = 10; // board feet
            public const float StumpHeight = 0.15F; // m
            public const float ScribnerShortLogLength = Constant.MetersPerFoot * 20.0F; // m
            public const float ScribnerTrimLongLog = Constant.MetersPerFoot * 1.0F - 0.0001F; // m with 100 μm margin for numerical precision
            public const float ScribnerTrimShortLog = Constant.MetersPerFoot * 0.5F - 0.0001F; // m with 100 μm margin for numerical precision
        }

        public static class Financial
        {
            public const float DefaultAnnualDiscountRate = 0.04F;
            public const float OregonForestProductsHarvestTax = 4.1322F; // US$/MBF, https://www.oregon.gov/dor/programs/property/Pages/timber-forest-harvest.aspx
        }

        public static class GeneticDefault
        {
            public const float CrossoverProbabilityEnd = 0.5F;
            public const float ExchangeProbabilityEnd = 0.1F;
            public const float ExchangeProbabilityStart = 0.0F;
            public const float ExponentK = -8.0F;
            public const float FlipProbabilityEnd = 0.9F; // ~0.85 best for constant probability
            public const float FlipProbabilityStart = 0.0F;
            public const float GenerationMultiplier = 5.5F;
            public const float GenerationPower = 0.6F;
            public const int InitializationClasses = 1;
            public const PopulationInitializationMethod InitializationMethod = PopulationInitializationMethod.DiameterClass;
            public const float MinimumCoefficientOfVariation = 0.000001F;
            public const int PopulationSize = 30;
            public const PopulationReplacementStrategy ReplacementStrategy = PopulationReplacementStrategy.ContributionOfDiversityReplaceWorst;
            public const float ReservedPopulationProportion = 1.0F;
        }

        public static class Grasp
        {
            public const float DefaultMinimumConstructionGreedinessForMaximization = 0.9F;
            public const float FullyGreedyConstructionForMaximization = 1.0F;
            public const float FullyRandomConstructionForMaximization = 0.0F;
            public const int MinimumTreesRandomized = 5; // see Heuristic.ConstructTreeSelection()
            public const float NoConstruction = -1.0F;
        }

        public static class HeuristicDefault
        {
            public const int FinancialIndex = 0;
            public const int FirstCircularIterationMultiplier = 20;
            public const int HeroMaximumIterations = 20;
            public const float InitialThinningProbability = 0.5F;
            public const int RotationIndex = 0;
        }

        public static class MalcolmKnapp
        {
            public static class TreeCondition
            {
                public const int Dead = 2;
            }
        }

        public static class Maximum
        {
            public const float DiameterIncrementInInches = 4.5F;
            public const float ExpansionFactor = 1000.0F;
            public const float HeightIncrementInFeet = 20.0F;
            public const float PlantingDensityInTreesPerHectare = 40000.0F; // sanity upper bound, 0.5 m spacing
            public const float Sdi = 1000.0F;
            public const float SiteIndexInFeet = 300.0F;
        }

        public static class Minimum
        {
            public const float SiteIndexInFeet = 4.5F;
        }

        public static class MonteCarloDefault
        {
            public const float AnnealingAlpha = 0.7F;
            public const int AnnealingAveragingWindowLength = 10;
            public const int AnnealingIterationsPerTemperature = 10;
            public const float AnnealingReheadBy = 0.33F;
            public const float DelugeFinalMultiplier = 1.75F;
            public const float DelugeInitialMultiplier = 1.25F;
            public const float DelugeLowerWaterBy = 0.0033F;
            public const int IterationMultiplier = 19;
            public const float RecordTravelAlpha = 0.75F;
            public const float RecordTravelRelativeIncrease = 0.0075F;
            public const float ReheatAfter = 1.6F;
            public const int StopAfter = 19;
        }

        public static class PrescriptionSearchDefault
        {
            public const float DefaultIntensityStepSize = 8.0F; // loose optimum for coordinate ascent solution quality given minimum step of 1%
            public const float MaximumIntensity = 80.0F;
            public const float MaximumIntensityStepSize = 100.0F; // default to no limiting by percentage
            public const float MinimumIntensity = 20.0F;
            public const float MinimumIntensityStepSize = 1.0F;
            public const float StepSizeMultiplier = 0.5F;
            public const PrescriptionUnits Units = PrescriptionUnits.StemPercentageRemoved;
        }

        public static class OpenXml
        {
            public const string Namespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            public static class Attribute
            {
                public const string CellReference = "r";
                public const string CellType = "t";
                public const string Reference = "ref";
            }

            public static class CellType
            {
                public const string SharedString = "s";
            }

            public static class Element
            {
                public const string Cell = "c";
                public const string CellValue = "v";
                public const string Dimension = "dimension";
                public const string Row = "row";
                public const string SharedString = "si";
                public const string SharedStringText = "t";
                public const string SheetData = "sheetData";
            }
        }

        public static class Psp
        {
            public const int DefaultNumberOfStandMeasurements = 8;

            public static class ColumnIndex
            {
                public const int Dbh = 11;
                public const int Plot = 5;
                public const int Species = 7;
                public const int Status = 10;
                public const int Tag = 8;
                public const int Year = 9;
            }

            public static class TreeStatus
            {
                public const int Dead = 6;
                public const int Fused = 3;
                public const int Ingrowth = 2;
                public const int Live = 1;
                public const int NotFound = 9;
            }
        }

        public static class Simd128x4
        {
            public const int MaskAllTrue = 0xf;
            public const byte Broadcast0toAll = 0; // 0 << 6  | 0 << 4 | 0 << 2 | 0
            public const int ShuffleRotateLower1 = 0x39; // 0 << 6 | 3 << 4 | 2 << 2 | 1
            public const int ShuffleRotateLower2 = 0x4e; // 1 << 6 | 0 << 4 | 3 << 2 | 2
            public const int ShuffleRotateLower3 = 0x93; // 2 << 6 | 1 << 4 | 0 << 2 | 3
            public const int Width = 4;
        }

        public static class TabuDefault
        {
            public const float EscapeAfter = 1000.0F * 1000.0F; // off by default, nominal on value: 0.06F
            public const float EscapeBy = 0.04F;
            public const float IterationMultiplier = 4.25F;
            public const float MaximumTenureRatio = 0.1F;
            public const TabuTenure Tenure = TabuTenure.Stochastic;
        }
    }
}
