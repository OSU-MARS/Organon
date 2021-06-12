﻿using System.Collections.Generic;
using System.Globalization;

namespace Osu.Cof.Ferm.Heuristics
{
    public class SingleTreeMoveLog : IHeuristicMoveLog
    {
        // if needed, also track tree's plot ID by move
        public List<int> TreeIDByMove { get; protected set; }

        public SingleTreeMoveLog()
        {
            this.TreeIDByMove = new List<int>();
        }

        public int LengthInMoves
        {
            get { return this.TreeIDByMove.Count; }
        }

        public string GetCsvHeader(string prefix)
        {
            return prefix + "TreeEvaluated";
        }

        public string GetCsvValues(HeuristicResultPosition position, int move)
        {
            return this.TreeIDByMove[move].ToString(CultureInfo.InvariantCulture);
        }
    }
}
