using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SqlServer.QueryStatsAnalysis.Library
{
    public class AnalyzeQuery
    {
        private readonly SqlConnectionStringBuilder _connStringBuilder;
        private readonly string _userName = null;
        private readonly SecureString _password = null;
        private static readonly object _lockObject = new object();
        private StringBuilder _statistics = new StringBuilder();
        bool? _isSysAdmin;

        public AnalyzeQuery(string serverName, string databaseName, string userName, SecureString password)
        {
            _connStringBuilder = new SqlConnectionStringBuilder
            {
                DataSource = serverName,
                InitialCatalog = databaseName,
                IntegratedSecurity = false
            };
            this._userName = userName;
            this._password = password;
            if (!this._password.IsReadOnly())
            {
                this._password.MakeReadOnly();
            }
        }

        public AnalyzeQuery(string serverName, string databaseName, string userName, string password)
        {
            _connStringBuilder = new SqlConnectionStringBuilder
            {
                DataSource = serverName,
                InitialCatalog = databaseName,
                IntegratedSecurity = false,
                AsynchronousProcessing = true
            };
            this._userName = userName;
            this._password = new SecureString();
            foreach (var c in password.ToCharArray())
            {
                this._password.AppendChar(c);
            }
            this._password.MakeReadOnly();
        }

        public AnalyzeQuery(string serverName, string databaseName)
        {
            _connStringBuilder = new SqlConnectionStringBuilder
            {
                DataSource = serverName,
                InitialCatalog = databaseName,
                IntegratedSecurity = true,
                AsynchronousProcessing = true
            };
        }

        public QueryStats GetQueryStats(string queryText, bool clearBuffersAndCache = false, CancellationTokenSource cancelToken = null)
        {
            var getstats = GetQueryStatsAsync(queryText, clearBuffersAndCache, cancelToken).Result;
            return getstats;
        }

        private async Task<QueryStats> GetQueryStatsAsync(string queryText, bool clearBuffersAndCache = false, CancellationTokenSource cancelToken = null)
        {
            XNamespace sp = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

            _statistics.Clear();
            XElement sqlPlan = null;
            XElement batchSequence = null;

            try
            {
                await Task.Run(() =>
                {
                    using (SqlConnection connection = new SqlConnection(_connStringBuilder.ConnectionString))
                    {
                        if (!_connStringBuilder.IntegratedSecurity)
                        {
                            connection.Credential = new SqlCredential(this._userName, this._password);
                        }

                        Server server = new Server(new ServerConnection(connection));
                        if (cancelToken != null)
                        {
                            cancelToken.Token.Register(() => server.ConnectionContext.Cancel());
                        }
                        if (!_isSysAdmin.HasValue)
                        {
                            _isSysAdmin = Convert.ToBoolean(server.ConnectionContext.ExecuteScalar("SELECT IS_SRVROLEMEMBER('sysadmin')"));
                        }

                        if (clearBuffersAndCache && _isSysAdmin.Value)
                        {
                            using (var dummyReader = server.ConnectionContext.ExecuteReader("DBCC DROPCLEANBUFFERS\r\nDBCC FREEPROCCACHE\r\n")) { }
                        }
                        using (var dummyReader = server.ConnectionContext.ExecuteReader("SET STATISTICS TIME, IO, XML ON\r\n")) { }

                        server.ConnectionContext.InfoMessage += ConnectionContext_InfoMessage;
                        using (var reader = server.ConnectionContext.ExecuteReader(queryText))
                        {
                            object lastValue = null;
                            do
                            {
                                if (reader.Read())
                                {
                                    lastValue = reader.GetValue(0);
                                }
                                //concatenate all the plans into one
                                if (Convert.ToString(lastValue).StartsWith("<ShowPlanXML "))
                                {
                                    string xml = Convert.ToString(lastValue);
                                    if (sqlPlan != null)
                                    {
                                        XElement tmpPlan = XElement.Parse(xml);
                                        var batch = tmpPlan.Descendants(sp + "Batch");
                                        batchSequence.Add(batch);
                                    }
                                    else
                                    {
                                        sqlPlan = XElement.Parse(xml);
                                        batchSequence = sqlPlan.Element(sp + "BatchSequence");
                                    }
                                }

                            } while (reader.NextResult());
                        }

                        using (var dummyReader = server.ConnectionContext.ExecuteReader("SET STATISTICS TIME, IO, XML OFF\r\n")) { }
                    }

                });

                return new QueryStats(sqlPlan?.ToString(), _statistics.ToString());
            }
            catch (Exception ex)
            {
                //really dont like testing for strings in the exception. wish they raised custom exceptions
                if ((ex.InnerException != null && ex.InnerException.Message.ToLower().Contains("operation cancelled by user.")) 
                    || ex.Message.ToLower().Contains("operation cancelled by user.")
                    || ex.Message.ToLower().Contains("a severe error occurred on the current command.  the results, if any, should be discarded."))
                {
                    //eat canceled exception
                    Console.WriteLine(ex.ToString());
                }
                else
                {
                    throw;
                }
            }
            return null;
        }

        private void QueryStatsCallbackFunction(IAsyncResult asyncResult)
        {

        }

        private void ConnectionContext_InfoMessage(object sender, SqlInfoMessageEventArgs e)
        {
            _statistics.AppendLine(e.Message);
        }
    }
}
