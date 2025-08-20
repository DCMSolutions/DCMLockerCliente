using DCMLocker.Server.Background;
using DCMLocker.Server.BaseController;
using DCMLocker.Server.Hubs;
using DCMLocker.Server.Webhooks;
using DCMLocker.Shared;
using DCMLocker.Shared.Locker;
using DCMLockerCommunication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Dynamic.Core.Tokenizer;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace DCMLocker.Server.Controllers
{

    [Route("ClientApp/[controller]")]
    [Route("KioskApp/[controller]")]
    [ApiController]
    public class LockerController : ControllerBase
    {
        private readonly ILogger _log;
        private readonly IDCMLockerController _driver;
        private readonly IHubContext<LockerHub, ILockerHub> _hubContext;
        private readonly TBaseLockerController _base;
        private readonly HttpClient _http;
        private readonly LogController _evento;
        private readonly SystemController _system;
        private readonly WebhookService _webhookService;

        /// <summary> -----------------------------------------------------------------------
        /// Constructor
        /// </summary>
        /// <param name="driver"></param>
        /// <param name="context2"></param>
        /// <param name="logger"></param>
        /// <param name="Base"></param>------------------------------------------------------
        public LockerController(IDCMLockerController driver, IHubContext<LockerHub, ILockerHub> context2, ILogger<LockerController> logger, TBaseLockerController Base, HttpClient http, LogController logController, SystemController system, WebhookService webhookService)
        {
            _driver = driver;
            _log = logger;
            _hubContext = context2;
            _base = Base;
            _http = http;
            _evento = logController;
            _system = system;
            _webhookService = webhookService;
        }
        //public LockerController(ILogger<LockerController> logger)
        //{

        //    _log = logger;
        //}
        /// <summary> -----------------------------------------------------------------------
        ///  Retorna el estado actual del locker
        /// </summary>
        /// <returns></returns> -------------------------------------------------------------
        [HttpGet]
        //[Authorize(Roles = "Admin")]
        public IEnumerable<LockerCU> Get()
        {
            _hubContext.Clients.All.AddClientGrupoLocker();
            LockerCU[] s = new LockerCU[16];

            for (int x = 0; x < 16; x++)
            {
                CU status = _driver.GetCUState(x);
                s[x] = new LockerCU() { CU = x, Box = new LockerBox[16] };
                for (int y = 0; y < 16; y++)
                {
                    s[x].Box[y] = new LockerBox();
                    s[x].Box[y].Box = y;
                    s[x].Box[y].Reservado = false;
                    s[x].Box[y].Door = status.DoorStatus[y];
                    s[x].Box[y].Sensor = status.SensorStatus[y];
                    s[x].Box[y].Id = x * 16 + y;


                }
            }

            return s;
        }

        [HttpGet("id")]
        //[Authorize(Roles = "Admin")]
        public IEnumerable<LockerCU> GetById(int id)
        {
            _hubContext.Clients.All.AddClientGrupoLocker();
            var y = id % 16;
            var x = (id - y) / 16;

            LockerCU[] s = new LockerCU[1];

            CU status = _driver.GetCUState(x);
            s[0] = new LockerCU() { CU = x, Box = new LockerBox[1] };

            s[0].Box[0] = new LockerBox();
            s[0].Box[0].Box = y;
            s[0].Box[0].Reservado = false;
            s[0].Box[0].Door = status.DoorStatus[y];
            s[0].Box[0].Sensor = status.SensorStatus[y];
            s[0].Box[0].Id = x * 16 + y;




            return s;
        }

        [HttpPost("token")]
        //[Authorize(Roles = "Admin, User")]
        public async Task<IActionResult> Test([FromBody] string Token)
        {
            // preguntar al servidor si abro algo con el token que recibo
            try
            {
                _evento.AddEvento(new Evento($"Pedido de validación token {Token}", "token"));
                var ortaWebhook = await _webhookService.SendWebhookAsync("PeticionToken", $"Se envió al servidor el token {Token}", new { Token = Token });

                int _CU;
                int _Box;
                Uri uri = new Uri(_base.Config.UrlServer, "api/locker");

                ServerToken serverComunication = new();

                serverComunication.NroSerie = _base.Config.LockerID;
                serverComunication.Token = Token;
                _http.Timeout = TimeSpan.FromSeconds(5);
                var response = await _http.PostAsJsonAsync(uri, serverComunication);

                bool hayCerraduras = _system.GetEstadoCerraduras() == "Conectadas";

                if (response.IsSuccessStatusCode && hayCerraduras)
                {
                    try
                    {
                        var serverResponse = await response.Content.ReadFromJsonAsync<ServerToken>();
                        if (serverResponse.Box != null)
                        {
                            //recibo el id de mentira y tengo que abrir un box
                            var _IdBox = _base.LockerMap.LockerMaps.Where(b => b.Value.IdBox == serverResponse.Box).First().Value.IdFisico;
                            if (_IdBox != null)
                            {
                                _CU = _IdBox.GetValueOrDefault() / 16;
                                _Box = _IdBox.GetValueOrDefault() % 16;
                                _driver.SetBox(_CU, _Box);
                                _evento.AddEvento(new Evento($"Respuesta al pedido de validación token {Token}: Aceptado box {serverResponse.Box}", "token"));
                                _webhookService.SendWebhook("RespuestaToken", $"Token {Token} aceptado: abre el box {serverResponse.Box}", new { Token = Token, Box = serverResponse.Box, Respuesta = "Aceptado" });
                                return Ok(serverResponse.Box);
                            }
                            else
                            {
                                _evento.AddEvento(new Evento($"Respuesta al pedido de validación token {Token}: Rechazado, el box enviado por el servidor no tiene id físico asignado", "token falla"));
                                _webhookService.SendWebhook("RespuestaToken", $"Token {Token}: Respuesta del servidor invalida, el box enviado ({serverResponse.Box}) no tiene id físico asignado", new { Token = Token, Box = serverResponse.Box, Respuesta = "Error: el box enviado por el servidor no tiene id físico asignado" });
                                return StatusCode(203);
                            }
                        }
                        else
                        {
                            _evento.AddEvento(new Evento($"Respuesta al pedido de validación token {Token}: Rechazado, no se recibió un box en la respuesta del servidor", "token falla"));
                            _webhookService.SendWebhook("RespuestaToken", $"Token {Token}: Respuesta del servidor invalida, el box enviado ({serverResponse.Box}) no tiene id físico asignado", new { Token = Token, Box = 0, Respuesta = "Error: el box enviado por el servidor no tiene id físico asignado" });
                            return StatusCode(203);

                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                        _evento.AddEvento(new Evento($"Respuesta al pedido de validación token {Token}: Rechazado, la respuesta del servidor tiene formato erróneo", "token falla"));
                        _webhookService.SendWebhook("RespuestaToken", $"Token {Token}: Respuesta del servidor invalida, formato erroneo", new { Token = Token, Box = 0, Respuesta = "Error: la respuesta del servidor tiene formato erróneo" });
                        return StatusCode((int)response.StatusCode);
                    }
                }
                else
                {
                    if (hayCerraduras)
                    {
                        _evento.AddEvento(new Evento($"Respuesta al pedido de validación token {Token}: Rechazado", "token"));
                        _webhookService.SendWebhook("RespuestaToken", $"Token {Token} rechazado", new { Token = Token, Box = 0, Respuesta = "Rechazado" });
                    }
                    else
                    {
                        _evento.AddEvento(new Evento($"Pedido de validación token {Token} ignorado por falta de conexión de cerraduras", "token"));
                        _webhookService.SendWebhook("RespuestaToken", $"Token {Token}: Respuesta del servidor ignorada por falta de conexión de cerraduras", new { Token = Token, Box = 0, Respuesta = "Ignorado: falta de conexión a las cerraduras" });
                    }

                    // Handle non-successful status codes, e.g., response.StatusCode, response.ReasonPhrase, etc.
                    Console.WriteLine($"Request failed with status code: {response.StatusCode}");
                    return StatusCode((int)response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                _evento.AddEvento(new Evento($"Respuesta al pedido de validación token {Token}: Rechazado, no hay conexión", "token falla"));
                _webhookService.SendWebhook("RespuestaToken", $"Token {Token}: Rechazado por falta de conexión", new { Token = Token, Box = 0, Respuesta = "Error: no hay conexión" });

                // Maneja errores de solicitud HTTP (por ejemplo, problemas de red, servidor inaccesible, etc.)
                Console.WriteLine("Error de solicitud HTTP: " + ex.Message);
                return StatusCode(403);

            }
            catch (Exception ex)
            {
                _evento.AddEvento(new Evento($"Respuesta al pedido de validación token {Token}: Rechazado, error inesperado", "token falla"));
                _webhookService.SendWebhook("RespuestaToken", $"Token {Token}: Rechazado por error inesperado", new { Token = Token, Box = 0, Respuesta = "Error: inesperado" });

                // Maneja otros errores no esperados
                Console.WriteLine("Error inesperado: " + ex.Message);
                return StatusCode(403);

            }
        }

        //Cosas de tamaños
        [HttpGet("tamaño")]
        public async Task<IActionResult> GetTamaños()
        {
            try
            {
                //var path = $"{_base.Config.UrlServer}api/size";
                Uri resultUri = new Uri(_base.Config.UrlServer, "api/size");
                var response = await _http.GetFromJsonAsync<List<Tamaño>>(resultUri);
                return Ok(response);
            }
            catch
            {
                return StatusCode(500); // En caso de un error, devolver un código de estado 500 (Internal Server Error).
            }
        }

        [HttpPost("tamaño")]
        public bool AgregarTamaño([FromBody] Tamaño tamaño)
        {
            try
            {
                string sf = Path.Combine(_base.PathBase, "tamaños.ans");
                if (System.IO.File.Exists(sf))
                {
                    string content = System.IO.File.ReadAllText(sf);
                    List<Tamaño> listaDeTamaños = JsonSerializer.Deserialize<List<Tamaño>>(content);
                    if (listaDeTamaños.Count > 0)
                    {
                        tamaño.Id = listaDeTamaños.Max(t => t.Id) + 1;
                    }
                    else
                    {
                        tamaño.Id = 1;
                    }
                    listaDeTamaños.Add(tamaño);
                    listaDeTamaños = listaDeTamaños.OrderBy(x => (x.Alto * x.Ancho * x.Profundidad)).ToList();
                    string s = JsonSerializer.Serialize<List<Tamaño>>(listaDeTamaños);

                    using (StreamWriter b = System.IO.File.CreateText(sf))
                    {
                        b.Write(s);
                    }

                    return true;
                }
                else
                {
                    tamaño.Id = 1;
                    List<Tamaño> listaDeTamaños = new List<Tamaño> { tamaño };
                    string s = JsonSerializer.Serialize<List<Tamaño>>(listaDeTamaños);

                    using (StreamWriter b = System.IO.File.CreateText(sf))
                    {
                        b.Write(s);
                    }
                    return true;
                }
            }
            catch (Exception er)
            {
                throw;
            }
        }

        [HttpPost("listaTamaños")]
        public bool SubirTamaños([FromBody] List<Tamaño> listaDeTamaños)
        {
            try
            {
                string sf = Path.Combine(_base.PathBase, "tamaños.ans");

                listaDeTamaños = listaDeTamaños.OrderBy(x => (x.Alto * x.Ancho * x.Profundidad)).ToList();

                string s = JsonSerializer.Serialize<List<Tamaño>>(listaDeTamaños);

                using (StreamWriter b = System.IO.File.CreateText(sf))
                {
                    b.Write(s);
                }

                return true;
            }
            catch (Exception er)
            {
                throw;
            }
        }


        [HttpPost("editTamaño")]
        public bool EditTamaño([FromBody] Tamaño tamaño)
        {
            try
            {
                string sf = Path.Combine(_base.PathBase, "tamaños.ans");
                if (System.IO.File.Exists(sf))
                {
                    string content = System.IO.File.ReadAllText(sf);
                    List<Tamaño> listaDeTamaños = JsonSerializer.Deserialize<List<Tamaño>>(content);

                    listaDeTamaños = listaDeTamaños.Where(x => x.Id != tamaño.Id).ToList();
                    listaDeTamaños.Add(tamaño);

                    listaDeTamaños = listaDeTamaños.OrderBy(x => (x.Alto * x.Ancho * x.Profundidad)).ToList();
                    string s = JsonSerializer.Serialize<List<Tamaño>>(listaDeTamaños);

                    using (StreamWriter b = System.IO.File.CreateText(sf))
                    {
                        b.Write(s);
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception er)
            {
                throw;
            }
        }

        [HttpPost("deletetamaño")]
        public bool DeleteTamaño([FromBody] Tamaño tamaño)
        {
            try
            {
                string sf = Path.Combine(_base.PathBase, "tamaños.ans");
                if (System.IO.File.Exists(sf))
                {
                    string content = System.IO.File.ReadAllText(sf);
                    List<Tamaño> listaDeTamaños = JsonSerializer.Deserialize<List<Tamaño>>(content);

                    listaDeTamaños = listaDeTamaños.Where(x => x.Id != tamaño.Id).ToList();

                    string s = JsonSerializer.Serialize<List<Tamaño>>(listaDeTamaños);

                    using (StreamWriter b = System.IO.File.CreateText(sf))
                    {
                        b.Write(s);
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception er)
            {
                throw;
            }
        }

        /// <summary> -----------------------------------------------------------------------
        ///  Apertura de un Box, el mismo es verificado si el usuario lo posee como 
        ///  pertenencia
        /// </summary>
        /// <param name="Box"></param>-------------------------------------------------------
        [HttpPost("OpenBox")]
        //[Authorize(Roles = "Admin, User")]
        public ActionResult Set([FromBody] BoxAccessToken Box)
        {
            try
            {
                //Controlo que el usuario posee al Box
                if (User.IsInRole("Admin"))
                {
                    _driver.SetBox(Box.CU, Box.Box);
                    return Ok();
                }
                else
                {
                    int IdBox = (Box.CU << 4) + Box.Box;
                    // verifico si el Box pertenece al usuario
                    if (!_base.LockerUser.Users[User.Identity.Name].IsLocked)
                    {
                        bool Pertenese = _base.IsBoxUser(IdBox, User.Identity.Name);
                        if (Pertenese)
                        {
                            if (!_base.LockerMap.LockerMaps[IdBox].IsUserFixed)
                            {
                                _base.BoxRemoveUser(IdBox, User.Identity.Name);

                                DeleteToken(User.Identity.Name, _base.TokenBox(IdBox.ToString()).ToString());



                            }
                            _driver.SetBox(Box.CU, Box.Box);


                            return Ok();
                        }
                        else return BadRequest("Caja no esta asignada al Usuario.");
                    }
                    else return BadRequest("Usuario bloqueado.");
                }
            }
            catch (Exception er)
            {
                return BadRequest($"Error: {er.Message}");
            }

        }
        [HttpGet("OpenBox")]
        //[Authorize(Roles = "Admin, User")]
        public ActionResult Open(int _CU, int _Box)
        {
            try
            {
                CU status = _driver.GetCUState(_CU);
                if (status.DoorStatus[_Box])
                {
                    _driver.SetBox(_CU, _Box);
                    return Ok("Locker abierto");
                }
                else
                {
                    _evento.AddEvento(new Evento("Falló el OpenBox del LockerController", "cerraduras falla"));
                    return BadRequest("Locker no disponible para su apertura");
                }
            }
            catch (Exception er)
            {
                _evento.AddEvento(new Evento("Falló el OpenBox del LockerController", "cerraduras falla"));
                return BadRequest(er.Message);
            }

        }
        /// <summary>-----------------------------------------------------------------------
        /// Retorna una llave para la apertura del Box
        /// </summary>
        /// <param name="IdBox"></param>
        /// <returns></returns>-------------------------------------------------------------
        [HttpGet("GenerateTokenKey")]
        //[Authorize(Roles = "User")]
        public string[] GenearateTokenKey(int IdBox)
        {
            string Token = _base.GetTokenBox(IdBox, "a@.");
            //string Token = _base.GetTokenBox(IdBox, User.Identity.Name);
            return new string[] { Token };
        }

        [HttpGet("GetMyTokens")]
        [Authorize(Roles = "User")]
        public TokenAccessBox[] GetMyTokens()
        {
            List<TokenAccessBox> retorno = new List<TokenAccessBox>();
            BoxTokenKey[] bt = _base.GetTokenFromUser(User.Identity.Name);
            foreach (BoxTokenKey t in bt)
            {
                retorno.Add(new TokenAccessBox()
                {
                    User = User.Identity.Name,
                    DTExpiration = t.DTExpiration,
                    Box = t.Box,
                    Token = t.Token,
                    Tag = t.Tag,
                    IsBoxTemporal = !_base.LockerMap.LockerMaps[t.Box].IsUserFixed
                });
            }
            return retorno.ToArray();
        }

        /// <summary>------------------------------------------------------------------------
        /// 
        /// </summary>
        /// <param name="Token"></param>
        /// <returns></returns>--------------------------------------------------------------
        [HttpPost("TokenKey")]
        public int TokenKey([FromBody] string Token)
        {

            try
            {
                BoxTokenKey AccessToken = _base.TokenBox(Token);
                int cu = (AccessToken.Box & 0xf0) >> 4;
                int box = AccessToken.Box & 0x0f;

                if (_base.LockerMap.LockerMaps[AccessToken.Box].State == "Esperando abrir")
                {
                    _base.LockerMap.LockerMaps[AccessToken.Box].State = "Entregado";
                }
                if (_base.LockerMap.LockerMaps[AccessToken.Box].State == "Esperando retiro")
                {

                    _base.LockerMap.LockerMaps[AccessToken.Box].State = "Libre";
                    _base.BoxRemoveUser(AccessToken.Box, AccessToken.User);
                    DeleteToken(User.Identity.Name, Token);

                }



                _base.Config.Save(_base.PathBase);
                _driver.SetBox(cu, box);
                return AccessToken.Box;
            }
            catch (Exception er)
            {
                Console.WriteLine("[TOKENKEY] " + er.Message);
                throw;
            }
        }

        [HttpGet("ChangeAdminPassword")]
        [Authorize(Roles = "Admin")]
        public bool ChangeAdminPassword(string newPass, string oldPass)
        {

            Console.WriteLine("-----------------------------------------------------------");
            try
            {
                string filePath = Path.Combine(_base.PathBase, "adminHash.ans");
                System.IO.File.WriteAllText(filePath, TLockerUser.GetPassHash(newPass));
                return true;
            }
            catch (System.Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
        }
        /// <summary> -----------------------------------------------------------------------
        ///  Retorna todos los Box que le pertenecen a un usuario
        /// </summary>
        /// <returns></returns> -------------------------------------------------------------
        [HttpGet("GetMyPerfil")]
        [Authorize(Roles = "Admin, User")]
        public LockerUserPerfil GetMyPerfil()
        {
            if (User.IsInRole("Admin"))
            {
                LockerUserPerfil retorno = new LockerUserPerfil() { Enable = true, Boxses = new int[0] };
                return retorno;
            }

            if (_base.LockerUser.Users.ContainsKey(User.Identity.Name))
            {
                LockerUserPerfil retorno = new LockerUserPerfil();
                retorno.Boxses = _base.GetUserBox(User.Identity.Name);
                retorno.IsLocked = _base.LockerUser.Users[User.Identity.Name].IsLocked;
                retorno.Enable = _base.LockerUser.Users[User.Identity.Name].Enable;
                return retorno;
            }
            return null;
        }

        [HttpGet("GetBoxSelfManagement")]
        [Authorize(Roles = "User")]
        public int[] GetBoxSelfManagement()
        {
            List<int> retorno = new List<int>();
            // Busco la canfiguracion de los BOX
            var r = from p in _base.LockerMap.LockerMaps where p.Value.Enable == true && p.Value.IsUserFixed == false select p.Key;
            // Verifico si estan sindo utilizados o no
            foreach (int box in r)
            {
                if (_base.BoxSelfManagement_IsFree(box))
                {
                    retorno.Add(box);
                }
            }
            return retorno.ToArray();
        }
        //[HttpPost("BoxSelfManagementReserve")]
        //[Authorize(Roles = "User")]
        //public ActionResult BoxSelfManagementReserve([FromBody] int boxadrr)
        //{
        //    _base.LockerMap.LockerMaps[boxadrr].State = "Asignado";
        //    bool x = _base.BoxSelfManagement_Reserve(boxadrr, User.Identity.Name);
        //    _base.LockerMap.Save(_base.PathBase);


        //    if (x)
        //    {
        //        //_driver.SetBox(((boxadrr & 0xf0) >> 4), boxadrr & 0x0f);
        //        return Ok();
        //    }

        //    else return BadRequest("No se pudo reservar");
        //}
        [HttpGet("BoxSelfManagementReserve")]
        //[Authorize(Roles = "User")]
        public ActionResult BoxSelfManagementReserve(int boxadrr, string user)
        {
            _base.LockerMap.LockerMaps[boxadrr].State = "Asignado";
            bool x = _base.BoxSelfManagement_Reserve(boxadrr, user);
            _base.LockerMap.Save(_base.PathBase);


            if (x)
            {
                //_driver.SetBox(((boxadrr & 0xf0) >> 4), boxadrr & 0x0f);
                return Ok();
            }

            else return BadRequest("No se pudo reservar");
        }


        /// <summary> -----------------------------------------------------------------------
        ///  Retorna todos los Box que le pertenecen a un usuario
        /// </summary>
        /// <returns></returns> -------------------------------------------------------------
        [HttpGet("GetPerfilFromUser")]
        //[Authorize(Roles = "Admin")]
        public LockerUserPerfil GetPerfilFromUser(string iduser)
        {
            string user = iduser.Replace("%40", "@").Replace("%_", ".");
            if (_base.LockerUser.Users.ContainsKey(user))
            {
                LockerUserPerfil retorno = new LockerUserPerfil();
                retorno.Boxses = _base.GetUserBox(user);
                retorno.IsLocked = _base.LockerUser.Users[user].IsLocked;
                retorno.Enable = _base.LockerUser.Users[user].Enable;

                return retorno;
            }

            return null;
        }
        /// <summary>--------------------------------------------------------------
        /// Retorna los usuarios de un box
        /// </summary>
        /// <param name="IdBox"></param>
        /// <returns></returns>----------------------------------------------------
        [HttpGet("GetUserFromBox")]
        [Authorize(Roles = "Admin")]
        public string[] GetUserFromBox(int IdBox)
        {
            return _base.UserFromBox(IdBox);
        }
        /// <summary>------------------------------------------------------------------------
        /// Buscador de usuarios
        /// </summary>
        /// <param name="keyword"></param>
        /// <returns></returns>--------------------------------------------------------------
        [HttpGet("Search")]
        [Authorize(Roles = "Admin")]
        public string[] Search(string keyword)
        {
            var r = from p in _base.LockerUser.Users where p.Key.StartsWith(keyword) select p.Key;
            return r.ToArray();
        }
        [HttpGet("GetAllUsers")]
        //[Authorize(Roles = "Admin")]
        public List<TLockerUser> GetAllUsers(string keyword)
        {
            //var r = from p in _base.LockerUser.Users where p.Key.StartsWith(keyword) select p.Key;
            return _base.LockerUser.Users.Values.ToList();
        }



        #region CONFIGURACION %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        /// <summary>---------------------------------------------------------------------
        /// 
        /// </summary>
        /// <returns></returns>-----------------------------------------------------------
        [HttpGet("GetLockerConfig")]
        //[Authorize(Roles = "Admin")]
        public LockerConfig GetLockerConfig()
        {
            LockerConfig retorno = _base.Config;
            return retorno;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        [HttpPost("SetLockerConfig")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SetLockerConfig([FromBody] LockerConfig data)
        {
            try
            {
                bool changeID = _base.Config.LockerID != data.LockerID;
                bool changeURL = _base.Config.UrlServer != data.UrlServer;
                var viejaURL = _base.Config.UrlServer;
                var viejoID = _base.Config.LockerID;

                if (changeID)
                {
                    _evento.AddEvento(new Evento($"Cambio de configuración: ID de locker, de {_base.Config.LockerID} a {data.LockerID}", "sistema"));
                    await _webhookService.SendWebhookAsync("ConfiguracionID", $"Se cambió la configuración del locker: el ID del locker cambió de {_base.Config.LockerID} a {data.LockerID}", new { Viejo = _base.Config.LockerID, Nuevo = data.LockerID });
                }
                if (changeURL)
                {
                    _evento.AddEvento(new Evento($"Cambio de configuración: URL de servidor, de {_base.Config.UrlServer} a {data.UrlServer}", "sistema"));
                    await _webhookService.SendWebhookAsync("ConfiguracionURL", $"Se cambió la configuración del locker: la URL del servidor cambió de {_base.Config.UrlServer} a {data.UrlServer}", new { Viejo = _base.Config.UrlServer, Nuevo = data.UrlServer });
                }

                _base.Config.LockerID = data.LockerID;
                _base.Config.LockerMode = data.LockerMode;
                _base.Config.LockerType = data.LockerType;
                _base.Config.IsConfirmarEmail = data.IsConfirmarEmail;
                _base.Config.SmtpServer.Asunto = data.SmtpServer.Asunto;
                _base.Config.SmtpServer.From = data.SmtpServer.From;
                _base.Config.SmtpServer.Host = data.SmtpServer.Host;
                _base.Config.SmtpServer.Port = data.SmtpServer.Port;
                _base.Config.SmtpServer.UserName = data.SmtpServer.UserName;
                _base.Config.SmtpServer.Password = data.SmtpServer.Password;
                _base.Config.SmtpServer.EnableSSL = data.SmtpServer.EnableSSL;
                _base.Config.UrlServer = data.UrlServer;
                _base.Config.Save(_base.PathBase);

                if (changeURL) await _webhookService.SendWebhookAsync("ConfiguracionURL", $"Se cambió la configuración del locker: la URL del servidor cambió de {viejaURL} a {data.UrlServer}", new { Viejo = viejaURL, Nuevo = data.UrlServer });
                if (changeID)
                {
                    await Task.Delay(1000);
                    await _webhookService.SendWebhookAsync("ConfiguracionID", $"Se cambió la configuración del locker: el ID del locker cambió de {viejoID} a {data.LockerID}", new { Viejo = viejoID, Nuevo = data.LockerID });
                }

                return Ok();
            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
            }
            return BadRequest();
        }

        /// <summary>--------------------------------------------------------------------
        ///  GetBoxConfig: Retorna la configuracion del Box
        /// </summary>
        /// <param name="IdBox"></param>
        /// <returns></returns>----------------------------------------------------------
        [HttpGet("GetBoxConfig")]
        public TLockerMap GetBoxConfig(int IdBox)
        {
            if (_base.LockerMap.LockerMaps.ContainsKey(IdBox))
            {
                return _base.LockerMap.LockerMaps[IdBox];
            }
            return null;
        }
        [HttpGet("GetBoxConfigPorId")]
        public TLockerMap GetBoxConfigPorId(int idBox)
        {
            TLockerMap lockerVacio = new();
            if (idBox == 0) return lockerVacio;
            foreach (var lockerMap in _base.LockerMap.LockerMaps.Values)
            {
                if (lockerMap.IdBox == idBox)
                {
                    return lockerMap;
                }
            }
            return lockerVacio;
        }

        [HttpGet("GetAllBoxConfig")]
        public List<TLockerMap> GetAllBoxConfig()
        {
            var response = _base.LockerMap.LockerMaps.Values.Where(x => x.IdFisico != null).OrderBy(x => x.IdBox).ToList();
            return response;
        }
        [HttpGet("GetIdSinAsignar")]
        public List<int> GetIdSinAsignar()
        {
            //creo la lista con todos los id fisicos disponibles.
            List<int> listaLibres = Enumerable.Range(1, 257).ToList();

            //creo una lista con los usados
            List<int> listaUsados = _base.LockerMap.LockerMaps.Values.ToList().Where(x => x.IdFisico != null).ToList().Select(x => x.IdBox).ToList();

            // elimino los usados de los libres
            listaLibres.RemoveAll(numero => listaUsados.Contains(numero));

            return listaLibres;
        }



        /// <summary>----------------------------------------------------------------------
        /// SetBoxConfig: Modifica la configuracion del Box
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>------------------------------------------------------------
        [HttpPost("SetBoxConfig")]
        public ActionResult SetBoxConfig([FromBody] TLockerMap data)
        {
            if (_base.LockerMap.LockerMaps.ContainsKey(data.IdBox))
            {
                var box = _base.LockerMap.LockerMaps.Where(x => x.Key == data.IdBox).First().Value;

                box.Enable = data.Enable;
                box.AlamrNro = data.AlamrNro;
                box.IsSensorPresent = data.IsSensorPresent;
                box.IsUserFixed = data.IsUserFixed;
                box.LockerType = data.LockerType;
                box.TempMax = data.TempMax;
                box.TempMin = data.TempMin;
                box.State = data.State;
                box.Size = data.Size;
                box.IdFisico = data.IdFisico;
                _base.LockerMap.Save(_base.PathBase);
                return Ok();
            }
            return BadRequest();
        }
        /// <summary>----------------------------------------------------------------------
        /// DeleteBoxConfig: Elimina la configuracion del Box
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>------------------------------------------------------------
        [HttpPost("DeleteBoxConfig")]
        public ActionResult DeleteBoxConfig([FromBody] TLockerMap data)
        {

            if (_base.LockerMap.LockerMaps.ContainsKey(data.IdBox))
            {
                _base.LockerMap.LockerMaps[data.IdBox].Enable = false;
                _base.LockerMap.LockerMaps[data.IdBox].AlamrNro = data.AlamrNro;
                _base.LockerMap.LockerMaps[data.IdBox].IsSensorPresent = data.IsSensorPresent;
                _base.LockerMap.LockerMaps[data.IdBox].IsUserFixed = data.IsUserFixed;
                _base.LockerMap.LockerMaps[data.IdBox].LockerType = data.LockerType;
                _base.LockerMap.LockerMaps[data.IdBox].TempMax = data.TempMax;
                _base.LockerMap.LockerMaps[data.IdBox].TempMin = data.TempMin;
                _base.LockerMap.LockerMaps[data.IdBox].State = data.State;
                _base.LockerMap.LockerMaps[data.IdBox].Size = 0;
                _base.LockerMap.LockerMaps[data.IdBox].IdFisico = null;
                _base.LockerMap.Save(_base.PathBase);
                return Ok();
            }
            return BadRequest();
        }
        /// <summary>-----------------------------------------------------------------------
        /// Asigna Usuario a una Caja
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>-------------------------------------------------------------
        [HttpPost("BoxToUser")]
        [Authorize(Roles = "Admin")]
        public ActionResult BoxToUser([FromBody] LockerUserBox data)
        {
            try
            {
                _base.LockerMap.LockerMaps[data.Box].State = "Asignado";

                _base.BoxToUser(data.Box, data.User);
                return Ok();
            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
            }
            return BadRequest();
        }
        /// <summary>----------------------------------------------------------------------
        /// Desasigna una caja del usuario
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>------------------------------------------------------------
        [HttpPost("BoxRemoveUser")]
        [Authorize(Roles = "Admin")]
        public ActionResult BoxRemoveUser([FromBody] LockerUserBox data)
        {
            try
            {

                if (_base.GetTokenFromUser(data.User).Where(x => x.Box == data.Box).FirstOrDefault() != null)
                {
                    string token = _base.GetTokenFromUser(data.User).Where(x => x.Box == data.Box).FirstOrDefault().Token;
                    DeleteToken(User.Identity.Name, token);
                }
                _base.BoxRemoveUser(data.Box, data.User);
                return Ok();
            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
            }
            return BadRequest();
        }
        /// <summary>----------------------------------------------------------------------
        /// Bloquea o desbloquea a un usuario
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>------------------------------------------------------------
        [HttpPost("SetUserLockstate")]
        [Authorize(Roles = "Admin")]
        public ActionResult SetUserLockstate([FromBody] string user)
        {
            try
            {
                if (_base.LockerUser.Users.ContainsKey(user))
                {
                    _base.LockerUser.Users[user].IsLocked = !_base.LockerUser.Users[user].IsLocked;
                    _base.LockerUser.Save(_base.PathBase);
                    return Ok();
                }
            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
            }
            return BadRequest("No se pudo cambiar el estado del usuario.");
        }

        /// <summary>----------------------------------------------------------------------
        /// Retorna las cajas con configuracion Fijas
        /// </summary>
        /// <returns></returns>------------------------------------------------------------
        [HttpGet("GetBoxesFixedState")]
        [Authorize(Roles = "Admin")]
        public BoxState[] GetBoxesFixedState()
        {
            List<BoxState> lista = new List<BoxState>();
            try
            {
                foreach (int box in _base.LockerMap.LockerMaps.Keys)
                {
                    //Busco todas las cajas fijas
                    if (_base.LockerMap.LockerMaps[box].Enable)
                    {
                        lista.Add(new BoxState() { Box = _base.LockerMap.LockerMaps[box].IdBox, IsAssigned = false, IsFixed = true });
                    }
                    //Verifico si estan ocupadas mediante el indice

                }
                foreach (BoxState s in lista)
                {
                    if (_base.IndexBox.ContainsKey(s.Box))
                    {
                        s.IsAssigned = _base.IndexBox[s.Box].Count > 0;
                    }
                }
                return lista.ToArray();
            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
            }
            return new BoxState[0];
        }

        [HttpGet("GetIdIdBoxList")]
        public List<int> GetIdIdBoxList()
        {
            //creo la lista con todos los id fisicos disponibles.
            List<int> listaLibres = Enumerable.Range(0, 257).ToList();

            //creo una lista con los usados
            List<int?> listaUsados = _base.LockerMap.LockerMaps.Values.ToList().Where(x => x.IdFisico != null).ToList().Select(x => x.IdFisico).ToList();

            // elimino los usados de los libres
            listaLibres.RemoveAll(numero => listaUsados.Contains(numero));

            return listaLibres;
        }




        #endregion %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        [HttpPost("DeleteToken")]
        //[Authorize(Roles = "Admin")]
        public ActionResult deletetoken([FromBody] string[] data)
        {
            try
            {
                string token = data[0];
                string user = data[1];

                DeleteToken(user, token);


            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
            }
            return Ok();
        }
        private bool DeleteToken(string user, string token)
        {

            try
            {
                BoxTokenKey AccessToken = _base.TokenBox(token);

                if (_base.LockerMap.LockerMaps[AccessToken.Box].State == "Esperando abrir")
                {
                    _base.LockerMap.LockerMaps[AccessToken.Box].State = "Asignado";
                }
                if (_base.LockerMap.LockerMaps[AccessToken.Box].State == "Esperando retiro")
                {
                    _base.LockerMap.LockerMaps[AccessToken.Box].State = "Entregado";
                }

                _base.Config.Save(_base.PathBase);
                string uhash = TLockerUser.GetPassHash(user);
                string fBox = $"token{token}.key";
                string Dir = Path.Combine(_base.PathBase, $"DK{uhash}", fBox);
                System.IO.File.Delete(Dir);
                return true;



            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
                return false;
            }
        }


        /// <summary>
        /// Cosas de version
        /// </summary>
        /// <returns></returns>

        [HttpGet("GetVersion")]
        public IActionResult GetVersion()
        {
            try
            {
                var configPath = "appsettings.json";
                var json = System.IO.File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = JsonNode.Parse(json)!.AsObject();

                if (root.TryGetPropertyValue("Version", out JsonNode? intervalNode))
                {
                    string version = intervalNode.GetValue<string>();
                    return Ok(version);
                }

                return NotFound("No se encontro el valor.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


        [HttpGet("GetFecha")]
        public IActionResult GetFecha()
        {
            try
            {
                var configPath = "appsettings.json";
                var json = System.IO.File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = JsonNode.Parse(json)!.AsObject();

                if (root.TryGetPropertyValue("Fecha", out JsonNode? intervalNode))
                {
                    string fecha = intervalNode.GetValue<string>();
                    return Ok(fecha);
                }

                return NotFound("No se encontro el valor.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("GetNroSerie")]
        public IActionResult GetNroSerie()
        {
            try
            {
                string nroSerie = _base.Config.LockerID;
                return Ok(nroSerie);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// ping al sv para ver si está
        /// </summary>
        /// <returns></returns>
        [HttpGet("PingServer")]
        public async Task<bool> pingSv()
        {
            try
            {
                var response = await _http.GetAsync($"{_base.Config.UrlServer}api/size");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
