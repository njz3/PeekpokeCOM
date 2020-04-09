using PeekpokeCOM.Shells;
using PeekpokeCOM.Utils;
using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PeekpokeCOM
{
    public partial class MainForm : Form
    {
        protected bool Running = false;

        protected Thread ManagerThread = null;
        public MainForm()
        {
            InitializeComponent();

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Logger.Loggers += LogToTxtBox;
            Logger.LogLevel = LogLevels.DEBUG;
            Logger.Start();
        }

        const int MAX_LOG_BUF = 1 << 17; // 128kB
        const int MIN_LOG_BUF = 1 << 11; // 2kB
        StringBuilder savedLog = new StringBuilder(MAX_LOG_BUF);
        bool newText = false;

        public void LogToTxtBox(string text)
        {
            lock (savedLog) {
                // Sanity cleanup if only 2k left in buffer
                if (savedLog.Length > (MAX_LOG_BUF - MIN_LOG_BUF)) {
                    savedLog.Remove(0, savedLog.Length - MIN_LOG_BUF);
                }
                savedLog.Append(DateTime.Now.ToLongTimeString());
                savedLog.Append(" | ");
                savedLog.AppendLine(text);
            }
            newText = true;
        }


        private void btnOpenCOM_Click(object sender, EventArgs e)
        {
            if (ManagerThread == null) {
                int.TryParse(txtSpeed.Text, out var speed);
                shell = new SerialShell(txtCOM.Text, speed);
                if (!shell.Open())
                    return;

                ManagerThread = new Thread(ThreadReadWriteCOM);
                Running = true;
                ManagerThread.Name = "COM manager";
                ManagerThread.Priority = ThreadPriority.BelowNormal;
                ManagerThread.IsBackground = true;
                ManagerThread.Start();
                btnOpenCOM.Text = "Close";
            } else {
                Running = false;
                if (shell!=null) {
                    shell.Close();
                }
                if (ManagerThread!=null) {
                    ManagerThread.Join();
                }
                ManagerThread = null;
                btnOpenCOM.Text = "Open";
            }

        }

        Shell shell = null;


        void ThreadReadWriteCOM()
        {
            while (Running) {
                try {
                    shell.ProcessOneMessage();
                } catch(Exception ex) {
                    Logger.Log("Error while reading:" + ex.Message);
                }
                Thread.Sleep(16);
            }
            shell.Close();
        }



        private void timerRefresh_Tick(object sender, EventArgs e)
        {
            if (!newText)
                return;
            newText = false;
            lock (savedLog) {
                this.txtLog.Text = savedLog.ToString();
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            Logger.Stop();
        }

        private void txtLog_TextChanged(object sender, EventArgs e)
        {
            // set the current caret position to the end
            txtLog.SelectionStart = txtLog.Text.Length;
            // scroll it automatically
            txtLog.ScrollToCaret();
        }
    }
}
