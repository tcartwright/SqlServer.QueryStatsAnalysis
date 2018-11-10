namespace SqlServer.QueryStatsAnalysis.Library
{
    internal interface IWaitDialog
    {
        void Update(int value, string message);
    }
}