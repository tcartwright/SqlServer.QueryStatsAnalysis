using System;

namespace SqlServer.QueryStatsAnalysis.Library
{
    [Serializable]
    public class Statement
    {
        public int statement_id { get; internal set; }
        public double statement_cost { get; internal set; }
        public int used_memory_grant_kb { get; internal set; }
        public int memory_grant_kb { get; internal set; }
        public double estimated_number_of_rows { get; internal set; }
        public string optimization_level { get; internal set; }
        public string optimization_abort_reason { get; internal set; }
        public string statement_type { get; internal set; }
        public string warnings { get; internal set; }
        public string missingIndexes { get; internal set; }
        public string statement_text { get; internal set; }

        public override string ToString()
        {
            if (statement_cost == 0)
            {
                return base.ToString();
            }
            else
            {
                return $"statement_id={statement_id};statement_cost ={statement_cost}";
            }
        }
    }
}