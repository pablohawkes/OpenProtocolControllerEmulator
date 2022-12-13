using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net;
using OpenProtocolInterpreter.Communication;
using OpenProtocolInterpreter;
using OpenProtocolInterpreter.Tool;
using OpenProtocolInterpreter.PowerMACS;
using static System.Windows.Forms.AxHost;
using System.Collections;
using System.Security.Cryptography;
using System.Web.UI.WebControls;
using OpenProtocolInterpreter.ParameterSet;
using OpenProtocolInterpreter.KeepAlive;
using OpenProtocolInterpreter.Job;

namespace OpenProtocolControllerEmulator
{
    public partial class FrmMain : Form
    {
        private readonly Socket _mainSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger("LogFileLogger");
        private static readonly log4net.ILog logOpenProtocol = log4net.LogManager.GetLogger("OpenProtocolLogger");

        Thread t;
        private ManualResetEvent _terminate = new ManualResetEvent(false);
        Socket source;

        bool FormIsClosing = false;

        private static System.Timers.Timer aTimer;
        private bool SocketOpenlastStatus = false;


        int revSupported = -1;
        string rbuType = "";


        public FrmMain()
        {
            InitializeComponent();
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            cmbOpenProtocolRevision.SelectedIndex = 5;
            cmbRbuType.SelectedIndex = 0;
        }


        public void StartListening()
        {
            try
            {
                t = new Thread(new ThreadStart(ThreadWorker));
                t.Start();
            }
            catch (Exception exc)
            {
                log.Error("Error on StartListening " + Name, exc);
            }
        }

        private void ThreadWorker()
        {
            string SocketIpAddress = "127.0.0.1";

            byte[] ip = new byte[4];
            int i = 0;
            foreach (var part in SocketIpAddress.Split('.'))
            {
                ip[i] = byte.Parse(part);
                i++;
            }

            var localEndPoint = new IPEndPoint(new IPAddress(ip), int.Parse(txtPortNumber.Text));

            log.Debug("Starting Threadworker - Port " + txtPortNumber.Text);
            StartSocket(localEndPoint);
        }

        public void StartSocket(IPEndPoint local)
        {
            try
            {
                _mainSocket.Bind(local);
                _mainSocket.Listen(10);

                while (!_terminate.WaitOne(0))
                {
                    //log.Debug("Socket Open. Waiting for client...");
                    source = _mainSocket.Accept();

                    var state = new SocketState(source);

                    source.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0, OnDataReceive, state);

                    //                    Thread.Sleep(1000);
                    Application.DoEvents();
                }

                log.Debug("StartSocket finished");
            }
            catch (Exception exc)
            {
                if (FormIsClosing)
                {
                    log.Debug("StartSocket stopped due application closing");
                }
                else
                {
                    log.Error("Error on Socket: " + exc.Message, exc);
                }
            }
        }

        private void OnDataReceive(IAsyncResult result)
        {
            //log.Debug("Event OnDataReceive: Message received");
            var state = (SocketState)result.AsyncState;
            try
            {
                if (state.SourceSocket.Connected)
                {
                    var bytesRead = state.SourceSocket.EndReceive(result);
                    if (bytesRead > 0)
                    {
                        var receivedMessage = System.Text.Encoding.Default.GetString(state.Buffer).Substring(0, bytesRead);
                        //log.Debug("Received Message: " + CleanStringForLog(receivedMessage));

                        var textToSend = ProcessMessage(receivedMessage);
                        //TODO

                        if (textToSend != null)
                        {
                            var res = state.SourceSocket.Send(textToSend);
                        }

                        state.SourceSocket.BeginReceive(state.Buffer, 0, state.Buffer.Length, 0, OnDataReceive, state);
                    }
                    /*
                    else
                    {
                        log.Debug("Event OnDataReceive: " + state.SourceSocket.RemoteEndPoint.AddressFamily.ToString() + " - "
                                                          + state.SourceSocket.Connected.ToString());
                    }
                    */
                }
                else
                {
                    log.Info("SourceSocket disconnected");
                }
            }
            catch (Exception exc)
            {
                log.Error("Error on OnDataReceive", exc);
                state.SourceSocket.Close();
            }

        }


        private void btnOpenCloseServer_Click(object sender, EventArgs e)
        {
            revSupported = int.Parse(cmbOpenProtocolRevision.Text);

            if (cmbRbuType.SelectedIndex > 0)
                rbuType = cmbRbuType.Text;
            else
                rbuType = "none";

            StartListening();
        }
        byte[] ProcessMessage(string message)
        {
            //MID 0001 Application Communication start
            if (message.Substring(4, 4) == "9999")
            {
                var interpreter = new MidInterpreter().UseAllMessages(new Type[] { typeof(Mid9999) });
                var myMid9999 = interpreter.Parse<Mid9999>(message);

                logOpenProtocol.Debug(message);
                logOpenProtocol.Info(myMid9999.Pack());

                return myMid9999.PackBytesWithNul();
            }

            //MID 0001 Application Communication start
            if (message.Substring(4, 4) == "0001")
            {
                var interpreter = new MidInterpreter().UseAllMessages(new Type[] { typeof(Mid0001) });
                var myMid0001 = interpreter.Parse<Mid0001>(message);

                var revAsked = myMid0001.Header.Revision;

                logOpenProtocol.Debug(message);
                logOpenProtocol.Info(myMid0001.Pack());

                if (revAsked > revSupported)
                {
                    //Send ERROR 97
                    var myMid0004 = new Mid0004(1, Error.MID_REVISION_UNSUPPORTED);

                    logOpenProtocol.Info(myMid0004.Pack());
                    return myMid0004.PackBytesWithNul();
                }
                else
                {
                    //Send CommandAccepted 0004
                    var myMid0002 = new Mid0002();
                    myMid0002.Header.Revision = revSupported;
                    myMid0002.CellId = 0;
                    myMid0002.ChannelId = 0;
                    myMid0002.ControllerName = "OpenProtocolContEmulator";
                    myMid0002.SupplierCode = "OPE";
                    myMid0002.OpenProtocolVersion = Application.ProductVersion;
                    myMid0002.ControllerSoftwareVersion = Application.ProductVersion;
                    myMid0002.ToolSoftwareVersion = Application.ProductVersion;
                    myMid0002.RBUType = rbuType;
                    myMid0002.ControllerSerialNumber = "132456789";
                    myMid0002.SystemType = SystemType.POWER_FOCUS_4000;
                    myMid0002.SystemSubType = SystemSubType.NORMAL_TIGHTENING_SYSTEM;
                    myMid0002.SequenceNumberSupport = false;
                    myMid0002.LinkingHandlingSupport = false;


                    logOpenProtocol.Info(myMid0002.Pack());
                    return myMid0002.PackBytesWithNul();
                }
            }


            //MID 0010 Parameter set ID upload request
            else if (message.Substring(4, 4) == "0010")
            {
                var interpreter = new MidInterpreter().UseAllMessages(new Type[] { typeof(Mid0010) });
                var myMid0010 = interpreter.Parse<Mid0010>(message);

                logOpenProtocol.Debug(message);
                logOpenProtocol.Info(myMid0010.Pack());


                //Send 0011
                var myMid0011 = new Mid0011();
                myMid0011.Header.Revision = revSupported;
                if (chkPset001.Checked) myMid0011.ParameterSets.Add(1);
                if (chkPset002.Checked) myMid0011.ParameterSets.Add(2);
                if (chkPset003.Checked) myMid0011.ParameterSets.Add(3);
                if (chkPset004.Checked) myMid0011.ParameterSets.Add(4);
                if (chkPset005.Checked) myMid0011.ParameterSets.Add(5);
                if (chkPset006.Checked) myMid0011.ParameterSets.Add(6);

                logOpenProtocol.Info(myMid0011.Pack());
                return myMid0011.PackBytesWithNul();
            }

            //MID 0031 Job ID upload reply
            else if (message.Substring(4, 4) == "0030")
            {
                var interpreter = new MidInterpreter().UseAllMessages(new Type[] { typeof(Mid0030) });
                var myMid0030 = interpreter.Parse<Mid0030>(message);

                logOpenProtocol.Debug(message);
                logOpenProtocol.Info(myMid0030.Pack());


                //Send 0031
                var myMid0031 = new Mid0031();
                myMid0031.Header.Revision = revSupported;
                if (chkJob001.Checked) myMid0031.JobIds.Add(1);
                if (chkJob002.Checked) myMid0031.JobIds.Add(2);
                if (chkJob003.Checked) myMid0031.JobIds.Add(3);
                if (chkJob004.Checked) myMid0031.JobIds.Add(4);
                if (chkJob005.Checked) myMid0031.JobIds.Add(5);
                if (chkJob006.Checked) myMid0031.JobIds.Add(6);

                logOpenProtocol.Info(myMid0031.Pack());
                return myMid0031.PackBytesWithNul();
            }

            //MID 0042 Disable tool
            if (message.Substring(4, 4) == "0042")
            {
                var interpreter = new MidInterpreter().UseAllMessages(new Type[] { typeof(Mid0042) });
                var myMid0042 = interpreter.Parse<Mid0042>(message);

                logOpenProtocol.Debug(message);
                logOpenProtocol.Info(myMid0042.Pack());

                /*
                if (XXXXXXXXXXXXXXXXXXXXXXXXXXX)
                {
                    //Send ERROR 79
                    var myMid0004 = new Mid0004(1, Error.COMMAND_FAILED);

                    logOpenProtocol.Info(myMid0004.Pack());
                    return myMid0004.PackBytesWithNul();
                }
                else
                {
                    */
                //Send CommandAccepted 0005
                var myMid0005 = new Mid0005();
                myMid0005.Header.Revision = revSupported;
                myMid0005.MidAccepted = 42;

                logOpenProtocol.Info(myMid0005.Pack());
                return myMid0005.PackBytesWithNul();
                //}
            }

            //MID 0043 Disable tool
            if (message.Substring(4, 4) == "0043")
            {
                var interpreter = new MidInterpreter().UseAllMessages(new Type[] { typeof(Mid0043) });
                var myMid0043 = interpreter.Parse<Mid0043>(message);


                logOpenProtocol.Debug(message);
                logOpenProtocol.Info(myMid0043.Pack());

                /*
                if (XXXXXXXXXXXXXXXXXXXXXXXXXXX)
                {
                    //Send ERROR 79
                    var myMid0004 = new Mid0004(1, Error.COMMAND_FAILED);

                    logOpenProtocol.Info(myMid0004.Pack());
                    return myMid0004.PackBytesWithNul();
                }
                else
                {
                    */
                //Send CommandAccepted 0005
                var myMid0005 = new Mid0005();
                myMid0005.Header.Revision = revSupported;
                myMid0005.MidAccepted = 43;

                logOpenProtocol.Info(myMid0005.Pack());
                return myMid0005.PackBytesWithNul();
                //}
            }

            else
            {
                log.Error("Unknown Message -  msg: " + message);
                return null;
            }
        }
    }
}
