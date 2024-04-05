using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DCMLockerCommunication;
using DCMLocker;
using Microsoft.AspNetCore.SignalR;
using DCMLocker.Server.Hubs;
using DCMLocker.Shared;

namespace DCMLocker.Server.Background
{
    public interface IDCMLockerController
    {
        CU GetCUState(int CUid);
        void SetBox(int CUid, int Box);
    }

    public class DCMLockerConector : IDCMLockerController
    {
        public CU GetCUState(int CUid)
        {
            return DCMLockerController.GetCUState(CUid);
        }
        public void SetBox(int CUid, int Box)
        {
            DCMLockerController.SetBox(CUid, Box);
        }
    }


    /// <summary> %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    /// Servicio Backgroud para atender al Hardware del locker
    /// </summary> %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    public class DCMLockerController : IHostedService, IDisposable
    {
        private readonly IHubContext<LockerHub, ILockerHub> _hubContext;
        static DCMLockerTCPDriver driver = new DCMLockerTCPDriver();
        //----------------------------------------------------------------------------
        /// <summary>
        /// CONSTRUCTOR
        /// Configura los eventos del driver
        /// </summary>
        // --------------------------------------------------------------------------
        public DCMLockerController(IHubContext<LockerHub, ILockerHub> context2)
        {
            _hubContext = context2;
            driver.OnConnection += Driver_OnConnection;
            driver.OnDisConnection += Driver_OnDisConnection;
            driver.OnError += Driver_OnError;
            driver.OnCUChange += Driver_OnCUChange;
        }

        //---------------------------------------------------------------------------
        /// <summary>
        /// Libera todos los recursos
        /// </summary>
        //---------------------------------------------------------------------------
        public void Dispose()
        {
            driver = null;
        }
        //---------------------------------------------------------------------------
        /// <summary>
        /// Atiende al evento Start de los servicios.
        /// Activa la comunicacion con el locker
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        //---------------------------------------------------------------------------
        public Task StartAsync(CancellationToken cancellationToken)
        {
            driver.IP = "192.168.2.178";
            driver.Port = 4001;

            driver.Start();

            return Task.CompletedTask;
        }
        //-----------------------------------------------------------------------------
        /// <summary>
        /// Atiende al evento Stop de los servicios
        /// Detiene la comunicacion con el locker
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        //-----------------------------------------------------------------------------
        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                driver.Stop();
            }
            catch (Exception er)
            {
                Console.WriteLine($"Error en STOP de servicio Background: {er.Message}");
            }
            return Task.CompletedTask;
        }
        //-----------------------------------------------------------------------------
        /// <summary>
        /// EVENTO: ERROR
        /// FUENTE: Driver de locker
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">EvtArgError</param>
        //-----------------------------------------------------------------------------
        private static void Driver_OnError(object sender, EventArgs e)
        {
            Console.WriteLine("ERROR:" + ((EvtArgError)e).Er.Message);
        }
        //------------------------------------------------------------------------------
        /// <summary>
        /// EVENTO: DISCONNECTION
        /// FUENTE: Driver de locker
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //------------------------------------------------------------------------------
        private static void Driver_OnDisConnection(object sender, EventArgs e)
        {
            Console.WriteLine("DESCONEXION");
        }
        //------------------------------------------------------------------------------
        /// <summary>
        /// EVENTO: CONNECTION
        /// FUENTE: Driver de locker
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //------------------------------------------------------------------------------
        private static void Driver_OnConnection(object sender, EventArgs e)
        {
            Console.WriteLine("--------- CONEXION-----------------");
        }
        //------------------------------------------------------------------------------
        /// <summary>
        /// EVENTO: CUCHANGE
        /// FUENTE: Driver de locker
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">EvtArgCUChange</param>
        //------------------------------------------------------------------------------
        private void Driver_OnCUChange(object sender, EventArgs e)
        {
            EvtArgCUChange v = (EvtArgCUChange)e;
            _hubContext.Clients.All.LockerUpdated(v.CU.ADDR, "Cambio");

            //Console.WriteLine("CAMBIO:");
            //for (int x = 0; x < 16; x++)
            //{
            //    Console.WriteLine($"PUERTAS [{x}]: {v.CU.DoorStatus[x]}");
            //    Console.WriteLine($"SENSOR [{x}]: {v.CU.SensorStatus[x]}");
            //}


        }

        public static CU GetCUState(int CUid)
        {
            return driver.GetStatus(CUid);
        }

        public static void SetBox(int CU, int Box)
        {
            driver.OpenBox(CU, Box);
        }
    }
}
