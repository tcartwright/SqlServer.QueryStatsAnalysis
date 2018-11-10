using SqlServer.QueryStatsAnalysis.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QueryStatsAnalysis
{
    public class FormResult
    {
        public DialogResult Result { get; set; }
        public QueryStats QueryStats { get; set; }
    }
}
