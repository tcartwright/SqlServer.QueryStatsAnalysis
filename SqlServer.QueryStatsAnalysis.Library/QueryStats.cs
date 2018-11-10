using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SqlServer.QueryStatsAnalysis.Library
{
#pragma warning disable IDE1006 // Naming Styles
    [Serializable]
    public class QueryStats
    {

        private QueryStats() { }
        public QueryStats(string executionPlan, string statistics)
        {
            this.execution_plan = executionPlan;
            this.statistics = statistics;

            this.ParseExecutionPlan(executionPlan);
            this.ParseStatistics(statistics);
        }
        public Guid StatsId { get; private set; } = Guid.NewGuid();
        public IList<Statement> execution_plan_statements { get; private set; }
        public string execution_plan { get; private set; }
        public string query_text { get; set; }
        public string statistics { get; private set; }
        public int parse_and_compile_cpu { get; private set; }
        public int parse_and_compile_elapsed { get; private set; }
        public double worker_time { get; private set; }
        public double execution_time { get; private set; }
        public int scan_count { get; private set; }
        public int logical_reads { get; private set; }
        public int physical_reads { get; private set; }

        private void ParseExecutionPlan(string executionPlan)
        {
            execution_plan_statements = GetStatements(executionPlan);
        }

        private IList<Statement> GetStatements(string executionPlan)
        {
            IList<Statement> statements = new List<Statement>();
            if (string.IsNullOrWhiteSpace(executionPlan)) { return statements; }

            XNamespace sp = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

            XElement ele = XElement.Parse(executionPlan);
            var stmts = ele.Descendants(sp + "StmtSimple");

            foreach (var stmt in stmts)
            {
                if (stmt != null)
                {
                    var memoryInfo = stmt.Descendants(sp + "MemoryGrantInfo").FirstOrDefault();
                    var warnings = stmt.Descendants(sp + "Warnings").FirstOrDefault();
                    var missingIndexes = stmt.Descendants(sp + "MissingIndexes").FirstOrDefault();

                    statements.Add(new Statement()
                    {
                        statement_id = stmt.TryGetAttributeValue("StatementId", 0),
                        statement_cost = Convert.ToDouble(stmt.Attribute("StatementSubTreeCost").Value),
                        statement_type = stmt.TryGetAttributeValue("StatementType", ""),
                        statement_text = stmt.TryGetAttributeValue("StatementText", ""),
                        estimated_number_of_rows = double.Parse(stmt.TryGetAttributeValue("StatementEstRows", "0"), NumberStyles.Float),
                        optimization_level = stmt.TryGetAttributeValue("StatementOptmLevel", ""),
                        optimization_abort_reason = stmt.TryGetAttributeValue("StatementOptmEarlyAbortReason", ""),
                        used_memory_grant_kb = memoryInfo.TryGetAttributeValue("MaxUsedMemory", 0),
                        memory_grant_kb = memoryInfo.TryGetAttributeValue("GrantedMemory", 0),
                        warnings = warnings?.ToString(),
                        missingIndexes = missingIndexes?.ToString()
                    });
                }
            }
            return statements;
        }

        private void ParseStatistics(string statistics)
        {
            int val = 0;
            var matches = Regex.Matches(statistics, ". Scan count (?<scans>\\d*), logical reads (?<logical_reads>\\d*), physical reads (?<physical_reads>\\d*),");
            foreach (Match match in matches)
            {
                val = 0;
                if (int.TryParse(match.Groups["scans"].Value, out val))
                {
                    scan_count += val;
                }
                val = 0;
                if (int.TryParse(match.Groups["logical_reads"].Value, out val))
                {
                    logical_reads += val;
                }
                val = 0;
                if (int.TryParse(match.Groups["physical_reads"].Value, out val))
                {
                    physical_reads += val;
                }
            }

            matches = Regex.Matches(statistics, @"SQL Server parse and compile time:\n   CPU time = (?<cpu>\d*) ms,  elapsed time = (?<exec>\d*) ms.");
            foreach (Match match in matches)
            {
                val = 0;
                if (int.TryParse(match.Groups["cpu"].Value, out val))
                {
                    parse_and_compile_cpu += val;
                }
                val = 0;
                if (int.TryParse(match.Groups["exec"].Value, out val))
                {
                    parse_and_compile_elapsed += val;
                }
            }

            matches = Regex.Matches(statistics, @"SQL Server Execution Times:\n   CPU time = (?<cpu>\d*) ms,  elapsed time = (?<exec>\d*) ms.");
            foreach (Match match in matches)
            {
                val = 0;
                if (int.TryParse(match.Groups["cpu"].Value, out val))
                {
                    worker_time += val;
                }
                val = 0;
                if (int.TryParse(match.Groups["exec"].Value, out val))
                {
                    execution_time += val;
                }
            }
        }
    }
#pragma warning restore IDE1006 // Naming Styles
}
