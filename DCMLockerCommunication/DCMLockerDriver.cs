using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace DCMLockerCommunication
{
    public class TStatus
    {
        public UInt16 Status { get; set; }
        public bool this[int i]
        {
            get
            {
                if (i > 15) throw new IndexOutOfRangeException();
                return (Status & (0x0001 << i)) != 0 ? true : false;
            }
            set
            {
                if (i > 15) throw new IndexOutOfRangeException();

                if (value)
                {
                    int offset = 0x0001 << i;
                    Status = (UInt16)(Status | offset);
                }
                else
                {
                    int offset = 0x0001 << i;
                    offset = ~offset;
                    Status = (UInt16)(Status & offset);
                }
            }
        }

    }

    public class CU
    {
        public byte ADDR { get; set; }
        public TStatus DoorStatus;
        public TStatus SensorStatus;


    }

    public abstract class DCMLockerDriver
    {
        public event EventHandler OnConnection;
        public event EventHandler OnDisConnection;
        public event EventHandler OnCUChange;
        public event EventHandler OnError;

        protected CU[] _CU;
        protected Queue<byte> _BoxActionQueue;
        protected object objsync;

        protected void SendOnConnection()
        {
            try
            {
                OnConnection?.Invoke(this, null);
            }
            catch { }
        }
        protected void SendOnDisConnection()
        {
            try
            {
                OnDisConnection?.Invoke(this, null);
            }
            catch { }
        }
        protected void SendOnCUChange(CU cu)
        {
            try
            {
                OnCUChange?.Invoke(this, new EvtArgCUChange() { CU = cu });
            }
            catch { }
        }
        protected void SendOnError(Exception er)
        {
            try
            {
                OnError?.Invoke(this, new EvtArgError() { Er = er });
            }
            catch { }
        }


        public DCMLockerDriver()
        {
            _BoxActionQueue = new Queue<byte>();
            objsync = new object();
            _CU = new CU[16];
            for (int x = 0; x < 16; x++)
            {
                _CU[x] = new CU();
                _CU[x].SensorStatus = new TStatus();
                _CU[x].DoorStatus = new TStatus();

            }
        }

        public CU GetStatus(int CU)
        {
            if (CU > 15) throw new IndexOutOfRangeException();

            CU retorno = new CU();
            retorno.DoorStatus = _CU[CU].DoorStatus;
            retorno.SensorStatus = _CU[CU].SensorStatus;
            return retorno;
        }



        public abstract void Start();
        public abstract void Stop();

        public void OpenBox(int CU, int Box)
        {
            if ((CU < 16) && (Box < 16))
            {
                byte g = (byte)((CU << 4) | Box);
                lock (objsync)
                {
                    if (!_BoxActionQueue.Contains(g))
                    {
                        _BoxActionQueue.Enqueue(g);
                    }
                }
            }
        }
    }

    //-------------------------------------------------------------------------------------------
    public class DCMLockerTCPDriver : DCMLockerDriver
    {
        public string IP { get; set; }
        public int Port { get; set; }


        Thread _work;
        public override void Start()
        {
            if (string.IsNullOrEmpty(IP)) throw new Exception("IP no establecida");
            if (Port <= 0) throw new Exception("Port no establecido");

            if (_work == null)
            {
                _work = new Thread(WorkTCP);
                _work.Start();
            }
        }

        public override void Stop()
        {
            if (_work != null)
            {
                _work.Interrupt();
                _work.Join();
                _work = null;
            }
        }

        async void WorkTCP(object state)
        {

            try
            {
                byte[] rx = new byte[1024];
                byte[] realrx = new byte[10];

                int rxstate = 0;
                // Buscamos CU por medio de TCP

                while (true)
                {
                    TcpClient Cliente = new TcpClient();
                    await Cliente.ConnectAsync(IPAddress.Parse(IP), Port);
                    DateTime timeref = DateTime.Now;
                    NetworkStream stream = Cliente.GetStream();
                    this.SendOnConnection();
                    try
                    {
                        while (Cliente.Connected)
                        {

                            if (_BoxActionQueue.Count > 0)
                            {
                                byte addr = 0;
                                lock (objsync)
                                {
                                    addr = _BoxActionQueue.Dequeue();
                                }
                                // Pregunto el estado del dispositivo
                                PTransporte trama = new PTransporte();
                                trama.DATA = new PLockerTablet()
                                {
                                    ADDR = addr,
                                    CMD = PLocker.enumCMD.DoorOpen
                                };
                                byte[] b = trama.ToArray();
                                stream.Write(b, 0, b.Length);
                            }


                            if (DateTime.Now.Subtract(timeref).TotalMilliseconds > 500)
                            {
                                timeref = DateTime.Now;
                                // Pregunto el estado del dispositivo
                                PTransporte trama = new PTransporte();
                                trama.DATA = new PLockerTablet()
                                {
                                    ADDR = 0xf0,
                                    CMD = PLocker.enumCMD.RqtLockAndInfraredStatus
                                };
                                byte[] b = trama.ToArray();
                                stream.Write(b, 0, b.Length);
                            }

                            if (Cliente.Available > 0)
                            {
                                int Reallen = stream.Read(rx, 0, Cliente.Available);
                                int y = 0;
                                for (int x = 0; x < 1000; x++)
                                {
                                    switch (rxstate)
                                    {
                                        case 0:
                                            y = 0;
                                            realrx[y] = rx[x];
                                            if (rx[x] == PTransporte.STX) rxstate = 1;
                                            break;
                                        case 1:
                                            y++;
                                            realrx[y] = rx[x];
                                            if (rx[x] == PTransporte.ETX) rxstate = 2;
                                            break;
                                        case 2:
                                            y++;
                                            realrx[y] = rx[x];
                                            PLockerBoard l = (PLockerBoard)PTransporte.GetPkt(realrx, y + 1);
                                            if (l!=null)
                                            {
                                                UInt16 sd = BitConverter.ToUInt16(l.STATUS, 0);
                                                UInt16 ss = BitConverter.ToUInt16(l.STATUS, 2);
                                                bool cuchange = false;
                                                if (this._CU[l.CU].DoorStatus.Status != sd)
                                                {
                                                    this._CU[l.CU].DoorStatus.Status = sd;
                                                    cuchange = true;
                                                }
                                                if (this._CU[l.CU].SensorStatus.Status != ss)
                                                {
                                                    this._CU[l.CU].SensorStatus.Status = ss;
                                                    cuchange = true;
                                                }
                                                if (cuchange) this.SendOnCUChange(this._CU[l.CU]);

                                                rxstate = 0;
                                            }
                                            
                                            break;

                                    }
                                }
                            }
                            else Thread.Sleep(100); // dormimos si no hay datos
                        }
                    }
                    catch (ThreadInterruptedException) { throw; }
                    catch (Exception er)
                    {
                        this.SendOnError(er);
                    }
                    this.SendOnDisConnection();
                    Thread.Sleep(100);
                }

            }
            catch 
            {
            }
        }
    }


    //-----------------------------------------------------------------------------
    public class DCMLockerCOMMDriver : DCMLockerDriver
    {
        public override void Start()
        {
            throw new NotImplementedException();
        }

        public override void Stop()
        {
            throw new NotImplementedException();
        }
    }


    //-------------------------------------------------------------------------------
    public class EvtArgCUChange : EventArgs
    {
        public CU CU { get; set; }
    }
    //-------------------------------------------------------------------------------
    public class EvtArgError : EventArgs
    {
        public Exception Er { get; set; }
    }
}
