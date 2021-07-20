using Osu.Cof.Ferm.Extensions;
using Osu.Cof.Ferm.Organon;
using System.Globalization;

namespace Osu.Cof.Ferm.Heuristics
{
    // arrays are allocated for all possible combinations of rotations and discount rates
    // This is likely to be an overallocation as thinnings will probably preclude some rotations, leading to unused elements within the arrays. For now,
    // it's assumed that the FIFO (first in, first out) length is short enough the memory required isn't a concern.
    public class PrescriptionFirstInFirstOutMoveLog : IHeuristicMoveLog
    {
        // first thin
        private readonly float[,,] fromAbovePercentageByRotationAndRate1;
        private readonly float[,,] fromBelowPercentageByRotationAndRate1;
        private readonly float[,,] proportionalPercentageByRotationAndRate1;

        // second thin
        private readonly float[,,] fromAbovePercentageByRotationAndRate2;
        private readonly float[,,] fromBelowPercentageByRotationAndRate2;
        private readonly float[,,] proportionalPercentageByRotationAndRate2;

        // third thin
        private readonly float[,,] fromAbovePercentageByRotationAndRate3;
        private readonly float[,,] fromBelowPercentageByRotationAndRate3;
        private readonly float[,,] proportionalPercentageByRotationAndRate3;

        private readonly int fifoLength;
        private readonly int[,,] moveIndexByRotationAndRate;
        private readonly int[,] setIndexByRotationAndRate;

        public int LengthInMoves { get; set; }

        public PrescriptionFirstInFirstOutMoveLog(int planningPeriods, int discountRates, int fifoLength)
        {
            this.fromAbovePercentageByRotationAndRate1 = new float[planningPeriods, discountRates, fifoLength];
            this.fromBelowPercentageByRotationAndRate1 = new float[planningPeriods, discountRates, fifoLength];
            this.proportionalPercentageByRotationAndRate1 = new float[planningPeriods, discountRates, fifoLength];

            this.fromAbovePercentageByRotationAndRate2 = new float[planningPeriods, discountRates, fifoLength];
            this.fromBelowPercentageByRotationAndRate2 = new float[planningPeriods, discountRates, fifoLength];
            this.proportionalPercentageByRotationAndRate2 = new float[planningPeriods, discountRates, fifoLength];

            this.fromAbovePercentageByRotationAndRate3 = new float[planningPeriods, discountRates, fifoLength];
            this.fromBelowPercentageByRotationAndRate3 = new float[planningPeriods, discountRates, fifoLength];
            this.proportionalPercentageByRotationAndRate3 = new float[planningPeriods, discountRates, fifoLength];

            this.fifoLength = fifoLength;
            this.moveIndexByRotationAndRate = new int[planningPeriods, discountRates, fifoLength];
            this.setIndexByRotationAndRate = new int[planningPeriods, discountRates];

            this.LengthInMoves = 0;

            ArrayExtensions.Fill(this.fromAbovePercentageByRotationAndRate1, -1.0F);
            ArrayExtensions.Fill(this.fromBelowPercentageByRotationAndRate1, -1.0F);
            ArrayExtensions.Fill(this.proportionalPercentageByRotationAndRate1, -1.0F);

            ArrayExtensions.Fill(this.fromAbovePercentageByRotationAndRate2, -1.0F);
            ArrayExtensions.Fill(this.fromBelowPercentageByRotationAndRate2, -1.0F);
            ArrayExtensions.Fill(this.proportionalPercentageByRotationAndRate2, -1.0F);

            ArrayExtensions.Fill(this.fromAbovePercentageByRotationAndRate3, -1.0F);
            ArrayExtensions.Fill(this.fromBelowPercentageByRotationAndRate3, -1.0F);
            ArrayExtensions.Fill(this.proportionalPercentageByRotationAndRate3, -1.0F);

            ArrayExtensions.Fill(this.moveIndexByRotationAndRate, -1);
            // this.setIndexByRotationAndRate can remain zeros
        }

        public string GetCsvHeader(string prefix)
        {
            return prefix + "Thin1above," + prefix + "Thin1proportional," + prefix + "Thin1below," +
                   prefix + "Thin2above," + prefix + "Thin2proportional," + prefix + "Thin2below," +
                   prefix + "Thin3above," + prefix + "Thin3proportional," + prefix + "Thin3below";
        }

        public string GetCsvValues(HeuristicResultPosition position, int move)
        {
            int rotationIndex = position.RotationIndex;
            int financialIndex = position.FinancialIndex;
            for (int moveIndex = 0; moveIndex < this.fifoLength; ++moveIndex)
            {
                if (move == this.moveIndexByRotationAndRate[rotationIndex, financialIndex, moveIndex])
                {
                    float fromAbovePercentage1 = this.fromAbovePercentageByRotationAndRate1[rotationIndex, financialIndex, moveIndex];
                    float proportionalPercentage1 = this.proportionalPercentageByRotationAndRate1[rotationIndex, financialIndex, moveIndex];
                    float fromBelowPercentage1 = this.fromBelowPercentageByRotationAndRate1[rotationIndex, financialIndex, moveIndex];
                    float fromAbovePercentage2 = this.fromAbovePercentageByRotationAndRate2[rotationIndex, financialIndex, moveIndex];
                    float proportionalPercentage2 = this.proportionalPercentageByRotationAndRate2[rotationIndex, financialIndex, moveIndex];
                    float fromBelowPercentage2 = this.fromBelowPercentageByRotationAndRate2[rotationIndex, financialIndex, moveIndex];
                    float fromAbovePercentage3 = this.fromAbovePercentageByRotationAndRate3[rotationIndex, financialIndex, moveIndex];
                    float proportionalPercentage3 = this.proportionalPercentageByRotationAndRate3[rotationIndex, financialIndex, moveIndex];
                    float fromBelowPercentage3 = this.fromBelowPercentageByRotationAndRate3[rotationIndex, financialIndex, moveIndex];

                    return fromAbovePercentage1.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                           proportionalPercentage1.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                           fromBelowPercentage1.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                           fromAbovePercentage2.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                           proportionalPercentage2.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                           fromBelowPercentage2.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                           fromAbovePercentage3.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                           proportionalPercentage3.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture) + "," +
                           fromBelowPercentage3.ToString(Constant.DefaultPercentageFormat, CultureInfo.InvariantCulture);
                }
            }

            return ",,,,,,,,";
        }

        public void SetPrescription(int move, ThinByPrescription? firstThinPrescription, ThinByPrescription? secondThinPrescription, ThinByPrescription? thirdThinPrescription)
        {
            this.SetPrescription(Constant.HeuristicDefault.RotationIndex, Constant.HeuristicDefault.FinancialIndex, move, firstThinPrescription, secondThinPrescription, thirdThinPrescription);
        }

        public void SetPrescription(int rotationIndex, int financialIndex, int move, ThinByPrescription? firstThinPrescription, ThinByPrescription? secondThinPrescription, ThinByPrescription? thirdThinPrescription)
        {
            // update specified discount rate
            int setIndex = this.setIndexByRotationAndRate[rotationIndex, financialIndex];
            this.moveIndexByRotationAndRate[rotationIndex, financialIndex, setIndex] = move;

            if (firstThinPrescription != null)
            {
                this.fromAbovePercentageByRotationAndRate1[rotationIndex, financialIndex, setIndex] = firstThinPrescription.FromAbovePercentage;
                this.proportionalPercentageByRotationAndRate1[rotationIndex, financialIndex, setIndex] = firstThinPrescription.ProportionalPercentage;
                this.fromBelowPercentageByRotationAndRate1[rotationIndex, financialIndex, setIndex] = firstThinPrescription.FromBelowPercentage;
            }
            else
            {
                this.fromAbovePercentageByRotationAndRate1[rotationIndex, financialIndex, setIndex] = 0.0F;
                this.proportionalPercentageByRotationAndRate1[rotationIndex, financialIndex, setIndex] = 0.0F;
                this.fromBelowPercentageByRotationAndRate1[rotationIndex, financialIndex, setIndex] = 0.0F;
            }

            if (secondThinPrescription != null)
            {
                this.fromAbovePercentageByRotationAndRate2[rotationIndex, financialIndex, setIndex] = secondThinPrescription.FromAbovePercentage;
                this.proportionalPercentageByRotationAndRate2[rotationIndex, financialIndex, setIndex] = secondThinPrescription.ProportionalPercentage;
                this.fromBelowPercentageByRotationAndRate2[rotationIndex, financialIndex, setIndex] = secondThinPrescription.FromBelowPercentage;
            }
            else
            {
                this.fromAbovePercentageByRotationAndRate2[rotationIndex, financialIndex, setIndex] = 0.0F;
                this.proportionalPercentageByRotationAndRate2[rotationIndex, financialIndex, setIndex] = 0.0F;
                this.fromBelowPercentageByRotationAndRate2[rotationIndex, financialIndex, setIndex] = 0.0F;
            }

            if (thirdThinPrescription != null)
            {
                this.fromAbovePercentageByRotationAndRate3[rotationIndex, financialIndex, setIndex] = thirdThinPrescription.FromAbovePercentage;
                this.proportionalPercentageByRotationAndRate3[rotationIndex, financialIndex, setIndex] = thirdThinPrescription.ProportionalPercentage;
                this.fromBelowPercentageByRotationAndRate3[rotationIndex, financialIndex, setIndex] = thirdThinPrescription.FromBelowPercentage;
            }
            else
            {
                this.fromAbovePercentageByRotationAndRate3[rotationIndex, financialIndex, setIndex] = 0.0F;
                this.proportionalPercentageByRotationAndRate3[rotationIndex, financialIndex, setIndex] = 0.0F;
                this.fromBelowPercentageByRotationAndRate3[rotationIndex, financialIndex, setIndex] = 0.0F;
            }

            // increment set index
            ++setIndex;
            if (setIndex >= this.fifoLength)
            {
                setIndex = 0;
            }
            this.setIndexByRotationAndRate[rotationIndex, financialIndex] = setIndex;
        }
    }
}
