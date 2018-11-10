using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlServer.QueryStatsAnalysis.Library
{
    public partial class WaitDialog : Form, IWaitDialog
    {
        private object _result;
        private Func<object> _action;
        private object _lockObj = new object();
        private System.Timers.Timer _timer;
        private CancellationTokenSource _cancelToken = null;
        private static Exception _exception;

        private WaitDialog(Form parent, Func<object> action, string caption, string message, CancellationTokenSource cancelToken = null)
        {
            InitializeComponent();

            this.Text = caption;
            lblMessage.Text = message;

            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(
              parent.Location.X + (parent.Width - this.Width) / 2,
              parent.Location.Y + (parent.Height - this.Height) / 2
            );

            _exception = null;
            _cancelToken = cancelToken;
            _action = action;
            btnCancel.Visible = cancelToken != null;
            btnCancel.UseWaitCursor = false;
            btnCancel.Cursor = Cursors.AppStarting;

            _timer = new System.Timers.Timer
            {
                AutoReset = false,
                Interval = 1,
                Enabled = true
            };
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
        }

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                _result = _action.Invoke();
            }
            catch (Exception ex)
            {
                _exception = ex;
            }

            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate ()
                {
                    this.Close();
                });
            }
            else
            {
                this.Close();
            }
        }

        private void frmWait_Activated(object sender, EventArgs e)
        {
            lblMessage.Refresh();
            this.Refresh();
        }

        public void Update(int value, string message)
        {
            this.progressBar1.Invoke((MethodInvoker)delegate
            {
                this.progressBar1.Value = value;
                this.lblMessage.Text = message;
            });
        }

        public static void ShowDialog<T>(Form parent, Func<T> action, string caption, string message, CancellationTokenSource cancelToken, out T result)
        {
            using (WaitDialog wait = new WaitDialog(parent, () => { return action(); }, caption, message, cancelToken))
            {
                wait.ShowDialog();

                if (_exception != null) { throw new ApplicationException("", _exception); }
                result = (T)wait._result;
            }

        }
        public static void ShowDialog(Form parent, Action action, string caption, string message, CancellationTokenSource cancelToken)
        {
            using (WaitDialog wait = new WaitDialog(parent, () => { action(); return null; }, caption, message, cancelToken))
            {
                wait.ShowDialog();
                if (_exception != null) { throw new ApplicationException("", _exception); }
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            _cancelToken.Cancel();
            this.lblMessage.Text = "Canceling ...";
        }
    }
}
