using Microsoft.Data.ConnectionUI;
using QueryStatsAnalysis.Properties;
using ScintillaNET;
using SqlServer.QueryStatsAnalysis.Library;
using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QueryStatsAnalysis
{
    public partial class frmMain : Form
    {
        private StringComparer _comparer = StringComparer.InvariantCultureIgnoreCase;
        private string _serverName;
        private string _databaseName;
        private string _userName;
        private SecureString _password;
        private bool _isTrusted;
        private QueryStats _results = null;
        private QueryStats _baseLineResults = null;


        public frmMain()
        {
            InitializeComponent();
        }

        #region events
        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                if (!(string.IsNullOrWhiteSpace(Settings.Default.ServerName) || string.IsNullOrWhiteSpace(Settings.Default.Database)))
                {
                    _serverName = Settings.Default.ServerName;
                    _databaseName = Settings.Default.Database;
                    if (!string.IsNullOrWhiteSpace(_serverName))
                    {
                        var builder = new SqlConnectionStringBuilder
                        {
                            DataSource = _serverName,
                            InitialCatalog = _databaseName,
                            IntegratedSecurity = true
                        };
                        ParseConnectionString(builder.ConnectionString);
                    }
                }

                InitialiseScintilla();
                SetupGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Exception in form load: " + ex.Message);
            }
        }

        private void btnRunQuery_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_serverName))
            {
                MessageBox.Show("Please connect to a database.");
                return;
            }
            if (string.IsNullOrWhiteSpace(txtQuery.Text))
            {
                MessageBox.Show("Please enter a query to run.");
                return;
            }

            try
            {
                //wait.Show();
                Cursor.Current = Cursors.WaitCursor;
                this.SuspendLayout();

                lblExecutionTime.Text = "0";
                lblLogicalReads.Text = "0";
                lblPhysicalReads.Text = "0";
                lblPlanCompileTime.Text = "0";
                lblScanCount.Text = "0";
                lblWorkerTime.Text = "0";

                dgvStatements.DataSource = null;
                dgvStatements.Rows.Clear();
                SetupGrid();

                AnalyzeQuery analyzeQuery;
                _results = null;

                try
                {
                    if (_isTrusted)
                    {
                        analyzeQuery = new AnalyzeQuery(_serverName, _databaseName);
                    }
                    else
                    {
                        analyzeQuery = new AnalyzeQuery(_serverName, _databaseName, _userName, _password);
                    }

                    var queryText = txtQuery.Text;
                    var clean = chkClean.Checked;
                    CancellationTokenSource cancelToken = new CancellationTokenSource();
                    WaitDialog.ShowDialog(this, 
                        () => analyzeQuery.GetQueryStats(queryText, clean, cancelToken), 
                        "Run Query", 
                        "Running query ...", 
                        cancelToken, 
                        out _results);
                   
                    if (_results == null)
                    {
                        //they canceled the query
                        MessageBox.Show("Operation canceled by user.");
                        return;
                    }
                    _results.query_text = txtQuery.Text;
                }
                catch (Exception ex)
                {
                    StringBuilder exMsg = new StringBuilder();
                    exMsg.AppendLine($"Exception running query: {ex.Message} \r\n");
                    while (ex.InnerException != null)
                    {
                        var sqlEx = ex.InnerException as SqlException;
                        if (sqlEx == null)
                        {
                            exMsg.AppendLine($"Message: {ex.InnerException.Message} \r\n");
                        }
                        else
                        {
                            exMsg.AppendLine($"Line: {sqlEx.LineNumber}, Message: {ex.InnerException.Message} \r\n");
                        }
                        ex = ex.InnerException;
                    }
                    MessageBox.Show(exMsg.ToString());
                    return;
                }

                lblExecutionTime.Text = Convert.ToString(_results.execution_time);
                lblLogicalReads.Text = Convert.ToString(_results.logical_reads);
                lblPhysicalReads.Text = Convert.ToString(_results.physical_reads);
                lblPlanCompileTime.Text = Convert.ToString(_results.parse_and_compile_elapsed);
                lblScanCount.Text = Convert.ToString(_results.scan_count);
                lblWorkerTime.Text = Convert.ToString(_results.worker_time);

                dgvStatements.DataSource = new SortableBindingList<Statement>(_results.execution_plan_statements);

                btnCloseDialog.Enabled
                    = btnSaveBaseLine.Enabled
                    = btnSaveToZip.Enabled = true;

                btnCompareToBaseLine.Enabled = _baseLineResults != null && _baseLineResults.StatsId != _results.StatsId;

                //MessageBox.Show("Done.");
            }
            finally
            {
                this.ResumeLayout();
                Cursor.Current = Cursors.Default;
            }
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            using (var dialog = new DataConnectionDialog())
            {
                // If you want the user to select from any of the available data sources, do this:
                //DataSource.AddStandardDataSources(dialog);

                // OR, if you want only certain data sources to be available
                // (e.g. only SQL Server), do something like this instead: 
                dialog.DataSources.Add(DataSource.SqlDataSource);
                dialog.DataSources.Add(DataSource.SqlFileDataSource);

                if (!(string.IsNullOrWhiteSpace(this._serverName) || string.IsNullOrWhiteSpace(this._databaseName)))
                {
                    var builder = new SqlConnectionStringBuilder();
                    builder.DataSource = _serverName;
                    builder.InitialCatalog = _databaseName;
                    builder.IntegratedSecurity = _isTrusted;
                    if (!_isTrusted)
                    {
                        builder.UserID = _userName;
                    }

                    dialog.SelectedDataSource = DataSource.SqlDataSource;
                    dialog.SelectedDataProvider = DataProvider.SqlDataProvider;
                    dialog.ConnectionString = builder.ConnectionString;
                }

                DialogResult userChoice = DataConnectionDialog.Show(dialog);

                // Return the resulting connection string if a connection was selected:
                if (userChoice == DialogResult.OK)
                {
                    var builder = new SqlConnectionStringBuilder(dialog.ConnectionString);
                    Settings.Default.ServerName = builder.DataSource;
                    Settings.Default.Database = builder.InitialCatalog;
                    Settings.Default.Save();
                    ParseConnectionString(dialog.ConnectionString);
                }
            }

        }

        private void tsmCopy_Click(object sender, EventArgs e)
        {
            if (_results == null)
            {
                MessageBox.Show("There are no query results to save.");
                return;
            }

            StringBuilder stats = new StringBuilder();

            stats.AppendLine($"Execution Time (ms)\t{_results.execution_time}");
            stats.AppendLine($"CPU/Worker Time (ms)\t{_results.worker_time}");
            stats.AppendLine($"Plan Compile Time (ms)\t{_results.parse_and_compile_elapsed}");
            stats.AppendLine($"Scan Count\t{_results.scan_count}");
            stats.AppendLine($"Logical Reads (pages)\t{_results.logical_reads}");
            stats.AppendLine($"Physical Reads (pages)\t{_results.physical_reads}");
            stats.AppendLine("\r\n\r\n");
            stats.AppendLine("Statement ID\tRow Count\tCost\tMemory Grant (KB)\tUsed Memory Grant (KB)\tOptimization Level\tOptimization Abort Reason\tStatement Type");
            foreach (var stmt in _results.execution_plan_statements)
            {
                stats.AppendLine($"{stmt.statement_id}\t{stmt.estimated_number_of_rows}\t{stmt.statement_cost}\t{stmt.memory_grant_kb}\t{stmt.used_memory_grant_kb}\t{stmt.optimization_level}\t{stmt.optimization_abort_reason}\t{stmt.statement_type}");
            }

            Clipboard.SetText(stats.ToString());
        }

        private void btnCloseDialog_Click(object sender, EventArgs e)
        {
            if (_results != null)
            {
                this.DialogResult = DialogResult.OK;
            }
            else
            {
                this.DialogResult = DialogResult.Abort;
            }
            this.Close();
        }

        private void btnSaveBaseLine_Click(object sender, EventArgs e)
        {
            if (_comparer.Equals(btnSaveBaseLine.Text, "Save Baseline"))
            {
                _baseLineResults = ObjectCopier.Clone(_results);
                btnSaveBaseLine.Text = "Clear Baseline";
            }
            else
            {
                _baseLineResults = null;
                btnCompareToBaseLine.Enabled = false;
                btnSaveBaseLine.Text = "Save Baseline";
            }
        }

        private void btnCompareToBaseLine_Click(object sender, EventArgs e)
        {
            if (_baseLineResults.StatsId == _results.StatsId)
            {
                MessageBox.Show("You are trying to compare the same statistics to itself. Please run another query to compare to.");
                return;
            }

            try
            {
                Action action = () =>
                {
                    string fileName = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(Path.GetTempFileName()) + ".html");
                    var comparisonReport = GenerateComparisonReport();
                    File.WriteAllText(fileName, comparisonReport);
                    Process.Start(fileName);
                };

                WaitDialog.ShowDialog(this, () => action(), "Generate Report", "Generating comparison report ...", null);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Exception generating report: {ex.Message}");
            }
        }

        private void btnSaveToZip_Click(object sender, EventArgs e)
        {
            if (_results == null)
            {
                MessageBox.Show("There are no query results to save.");
                return;
            }

            try
            {

                saveFileDialog1.DefaultExt = ".zip";
                saveFileDialog1.Filter = "Zip File|*.zip";
                saveFileDialog1.FileName = "Save Query Stats";
                saveFileDialog1.OverwritePrompt = true;

                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    Action action = () =>
                    {
                        var fileName = saveFileDialog1.FileName;
                        if (File.Exists(fileName)) { File.Delete(fileName); }
                        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
                        Directory.CreateDirectory(dir);

                        if (_baseLineResults != null && _baseLineResults.StatsId != _results.StatsId)
                        {
                            File.WriteAllText(Path.Combine(dir, "baseline.sql"), _baseLineResults.query_text);
                            File.WriteAllText(Path.Combine(dir, "baselineStats.rpt"), _baseLineResults.statistics);
                            File.WriteAllText(Path.Combine(dir, "baselineStats.sqlplan"), _baseLineResults.execution_plan);
                            File.WriteAllText(Path.Combine(dir, "ComparisonReport.html"), GenerateComparisonReport());
                            SaveStatisticsReport(Path.Combine(dir, "baselineStats.csv"), _baseLineResults);
                        }

                        File.WriteAllText(Path.Combine(dir, "query.sql"), _results.query_text);
                        File.WriteAllText(Path.Combine(dir, "queryStats.rpt"), _results.statistics);
                        File.WriteAllText(Path.Combine(dir, "queryStats.sqlplan"), _results.execution_plan);
                        SaveStatisticsReport(Path.Combine(dir, "queryStats.csv"), _results);

                        ZipFile.CreateFromDirectory(dir, fileName);

                        Directory.Delete(dir, true);
                    };

                    WaitDialog.ShowDialog(this, () => action(), "Save To Zip", "Saving statistics to zip ...", null);

                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Exception saving file: {ex.Message}");
            }
        }
        #endregion events

        #region methods
        public static FormResult Execute(string connectionString, string query)
        {
            using (var frm = new frmMain())
            {
                if (string.IsNullOrWhiteSpace(Settings.Default.ServerName))
                {
                    frm.ParseConnectionString(connectionString);
                }
                else
                {
                    //they have an existing server name..., use it. just reset the database. they might have connected to a different server
                    var builder = new SqlConnectionStringBuilder(connectionString);
                    builder.DataSource = Settings.Default.ServerName;
                    frm.ParseConnectionString(builder.ConnectionString);
                }

                frm.txtQuery.Text = query;
                frm.btnCloseDialog.Visible = true;

                var formResult = new FormResult
                {
                    Result = frm.ShowDialog()
                };

                if (formResult.Result == DialogResult.OK)
                {
                    formResult.QueryStats = frm._results;
                    formResult.QueryStats.query_text = frm.txtQuery.Text;
                }
                return formResult;
            }
        }

        private string AddStatements(string template, string rowsName, QueryStats stats)
        {
            var rows = stats.execution_plan_statements.Select(s => $"<tr><td>{s.statement_id}</td><td{GetCostStyle(s.statement_cost)}>{Convert.ToDouble(s.statement_cost).ToString("#0.0000")}</td><td>{Convert.ToDouble((s.used_memory_grant_kb / 1024.0)).ToString("#,##0.00")}</td><td{GetAbortStyle(s.optimization_abort_reason)}>{s.optimization_abort_reason}</td></tr>");
            template = template.Replace(rowsName, string.Join("", rows));
            return template;
        }

        private object GetAbortStyle(string optimization_abort_reason)
        {
            if (_comparer.Equals(optimization_abort_reason, "TimeOut"))
            {
                return " class=\"alert-danger\"";
            }

            return " class=\"alert-success\"";
        }

        private string GetCostStyle(double statement_cost)
        {
            if (statement_cost >= 300)
            {
                return " class=\"alert-danger\"";
            }
            else if (statement_cost >= 100)
            {
                return " class=\"alert-warning\"";
            }

            return " class=\"alert-success\"";
        }

        private string ReplaceTemplateParameter(string template, string parameterName, double parameter1, double parameter2, Func<double, string> format = null)
        {
            double improvementPct = (parameter1 - parameter2) / parameter2;
            if (Double.IsNaN(improvementPct)) { improvementPct = 0; }

            var style = " class=\"alert-info\"";
            if (improvementPct > 0 || double.IsInfinity(improvementPct))
            {
                style = " class=\"alert-success\"";
            }
            else if (improvementPct < 0)
            {
                style = " class=\"alert-danger\"";
            }
            string improvement = double.IsInfinity(improvementPct) ? "Infinity" : improvementPct.ToString("#,##0.00%");
            template = template
                .Replace($"<!--{{{parameterName}_old}}-->", format == null ? Convert.ToString(parameter1) : format(parameter1))
                .Replace($"<!--{{{parameterName}_new}}-->", format == null ? Convert.ToString(parameter2) : format(parameter2))
                .Replace($"<!--{{{parameterName}_delta}}-->", $"{improvement}")
                .Replace($"<!--{{{parameterName}_style}}-->", $"{style}");
            return template;
        }

        private void ParseConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return;
            }
            var _builder = new SqlConnectionStringBuilder(connectionString);
            _isTrusted = _builder.IntegratedSecurity;
            _serverName = _builder.DataSource;
            _databaseName = _builder.InitialCatalog;
            _userName = null;
            _password = null;

            if (!_isTrusted)
            {
                _userName = _builder.UserID;
                var passChars = _builder.Password.ToCharArray();
                if (passChars.Length > 0)
                {
                    _password = new SecureString();
                    foreach (var c in _builder.Password.ToCharArray())
                    {
                        _password.AppendChar(c);
                    }
                    _password.MakeReadOnly();
                }
            }
            tsslServerName.Text = _serverName;
            tsslDatabaseName.Text = _databaseName;
            tsslLogin.Text = _isTrusted ? $"{Environment.UserDomainName}\\{Environment.UserName}" : _userName;
        }

        private void InitialiseScintilla()
        {
            //Set the line numbers 
            txtQuery.Margins[0].Width = 32;//show line numbers https://github.com/jacobslusser/ScintillaNET/wiki/Displaying-Line-Numbers

            txtQuery.StyleResetDefault();
            txtQuery.Styles[Style.Default].Font = "Consolas";
            txtQuery.Styles[Style.Default].Size = 10;
            txtQuery.Styles[Style.Default].BackColor = Color.FromArgb(41, 49, 52); //Color.DarkSlateGray; 
            txtQuery.Styles[Style.Default].ForeColor = Color.FromArgb(87, 166, 74);
            txtQuery.StyleClearAll();
            txtQuery.CaretForeColor = Color.FromArgb(255, 255, 255);

            txtQuery.Styles[Style.Sql.Default].ForeColor = Color.FromArgb(255, 255, 255);
            txtQuery.Styles[Style.Sql.Word].ForeColor = Color.FromArgb(147, 199, 99);
            txtQuery.Styles[Style.Sql.Word].Bold = true;
            txtQuery.Styles[Style.Sql.Word2].ForeColor = Color.FromArgb(86, 156, 214);
            txtQuery.Styles[Style.Sql.Word2].Bold = true;
            txtQuery.Styles[Style.Sql.User1].ForeColor = Color.FromArgb(201, 117, 213);
            txtQuery.Styles[Style.Sql.User1].Bold = true;

            txtQuery.Styles[Style.Sql.Identifier].ForeColor = Color.FromArgb(220, 220, 220);
            txtQuery.Styles[Style.Sql.QuotedIdentifier].ForeColor = Color.FromArgb(220, 220, 220);

            txtQuery.Styles[Style.Sql.Character].ForeColor = Color.FromArgb(203, 65, 65);
            txtQuery.Styles[Style.Sql.Number].ForeColor = Color.FromArgb(255, 205, 34);
            txtQuery.Styles[Style.Sql.Operator].ForeColor = Color.FromArgb(180, 180, 180);
            txtQuery.Styles[Style.Sql.Comment].ForeColor = Color.FromArgb(87, 166, 74);
            txtQuery.Styles[Style.Sql.CommentLine].ForeColor = Color.FromArgb(87, 166, 74);

            txtQuery.Styles[Style.Sql.String].ForeColor = Color.FromArgb(0, 255, 255);

            //pulled these keywords from np++ AND they must be in lowercase for scintillanet
            txtQuery.SetKeywords(0, Resources.KEYWORDS.ToLower());
            txtQuery.SetKeywords(1, Resources.KEYWORDS2.ToLower());
            txtQuery.SetKeywords(4, Resources.KEYWORDS3.ToLower());
        }

        private void SetupGrid()
        {
            dgvStatements.Columns.Clear();
            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "statement_id",
                HeaderText = "Statement ID",
                Width = 90
            });

            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "estimated_number_of_rows",
                HeaderText = "Row Count",
                Width = 90
            });

            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "statement_cost",
                HeaderText = "Cost",
                Width = 90
                //,DefaultCellStyle.Format = "#.#000";    // Format 
            });

            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "memory_grant_kb",
                HeaderText = "Memory Grant (KB)",
                Width = 90
            });

            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "used_memory_grant_kb",
                HeaderText = "Used Memory Grant (KB)",
                Width = 90
            });

            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "optimization_level",
                HeaderText = "Optimization Level",
                Width = 90
            });

            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "optimization_abort_reason",
                HeaderText = "Optimization Abort Reason",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            });

            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "statement_type",
                HeaderText = "Statement Type",
                Width = 120
            });

            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "warnings",
                HeaderText = "Warnings",
                Width = 120,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "missingIndexes",
                HeaderText = "Missing Indexes",
                Width = 120,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            dgvStatements.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "statement_text",
                HeaderText = "Statement Text",
                Width = 120,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private string GenerateComparisonReport()
        {
            string comparisonTemplate = Resources.ComparisonTemplate;

            comparisonTemplate = comparisonTemplate
                .Replace("<!--{ServerName}-->", _serverName)
                .Replace("<!--{DatabaseName}-->", _databaseName)
                .Replace("<!--{UserName}-->", Environment.UserName)
                .Replace("<!--{Date}-->", DateTime.Now.ToShortDateString());

            comparisonTemplate = ReplaceTemplateParameter(comparisonTemplate, "exec_time", _baseLineResults.execution_time, _results.execution_time, (v) => TimeSpan.FromMilliseconds(v).ToString(@"hh\:mm\:ss\.fff"));
            comparisonTemplate = ReplaceTemplateParameter(comparisonTemplate, "cpu_time", _baseLineResults.worker_time, _results.worker_time, (v) => TimeSpan.FromMilliseconds(v).ToString(@"hh\:mm\:ss\.fff"));
            comparisonTemplate = ReplaceTemplateParameter(comparisonTemplate, "plan_compile_time", _baseLineResults.parse_and_compile_cpu, _results.parse_and_compile_cpu, (v) => TimeSpan.FromMilliseconds(v).ToString(@"hh\:mm\:ss\.fff"));
            comparisonTemplate = ReplaceTemplateParameter(comparisonTemplate, "scan_count", _baseLineResults.scan_count, _results.scan_count);
            comparisonTemplate = ReplaceTemplateParameter(comparisonTemplate, "logical_reads", _baseLineResults.logical_reads, _results.logical_reads);
            comparisonTemplate = ReplaceTemplateParameter(comparisonTemplate, "physical_reads", _baseLineResults.physical_reads, _results.physical_reads);
            comparisonTemplate = ReplaceTemplateParameter(comparisonTemplate, "physical_reads_mb", _baseLineResults.physical_reads / 128.0, _results.physical_reads / 128.0);

            comparisonTemplate = AddStatements(comparisonTemplate, "<!--{OldStatementRows}-->", _baseLineResults);
            comparisonTemplate = AddStatements(comparisonTemplate, "<!--{NewStatementRows}-->", _results);
            return comparisonTemplate;
        }

        private void SaveStatisticsReport(string fileName, QueryStats queryStats)
        {
            var sb = new StringBuilder();

            // append the total statistics
            sb.AppendLine($"\"Execution Time (ms)\", {queryStats.execution_time}");
            sb.AppendLine($"\"CPU/Worker Time (ms)\", {queryStats.worker_time}");
            sb.AppendLine($"\"Plan Compile Time (ms)\", {queryStats.parse_and_compile_elapsed}");
            sb.AppendLine($"\"Scan Count\", {queryStats.scan_count}");
            sb.AppendLine($"\"Logical Reads (pages)\", {queryStats.logical_reads}");
            sb.AppendLine($"\"Physical Reads (pages)\", {queryStats.physical_reads}");
            sb.AppendLine(",,");
            sb.AppendLine(",,");
            // append the statement headers
            sb.Append("\"Statement ID\",");
            sb.Append("\"Row Count\",");
            sb.Append("\"Cost\",");
            sb.Append("\"Memory Grant (KB)\",");
            sb.Append("\"Used Memory Grant (KB)\",");
            sb.Append("\"Optimization Level\",");
            sb.Append("\"Optimization Abort Reason\",");
            sb.Append("Statement Type\",");
            sb.Append("\"Warnings\",");
            sb.Append("\"Missing Indexes\",");
            sb.Append("\"Statement Text\"");
            sb.AppendLine("");
            foreach (var statement in queryStats.execution_plan_statements)
            {
                sb.Append($"{statement.statement_id},");
                sb.Append($"{statement.estimated_number_of_rows},");
                sb.Append($"{statement.statement_cost},");
                sb.Append($"{statement.memory_grant_kb},");
                sb.Append($"{statement.used_memory_grant_kb},");
                sb.Append($"{statement.optimization_level},");
                sb.Append($"\"{statement.optimization_abort_reason}\",");
                sb.Append($"{statement.statement_type},");
                sb.Append($"\"{statement.warnings}\",");
                sb.Append($"\"{statement.missingIndexes}\",");
                sb.Append($"\"{statement.statement_text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ')}\"");
                sb.AppendLine("");
            }

            File.WriteAllText(fileName, sb.ToString());
        }

        #endregion methods

    }
}
