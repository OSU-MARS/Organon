namespace Osu.Cof.Ferm.Organon
{
    public interface IHarvest
    {
        int Period { get; set; }

        IHarvest Clone();
        void CopyFrom(IHarvest other);
        float EvaluateTreeSelection(OrganonStandTrajectory trajectory);
    }
}
