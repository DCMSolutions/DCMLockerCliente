using DCMLocker.Server.Background;
using DCMLocker.Server.BaseController;
using DCMLocker.Server.Hubs;
using DCMLocker.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using DCMLockerCommunication;
using System.Linq;
using DCMLocker.Server.Webhooks;

namespace DCMLocker.Server.Controllers
{
    [Route("ClientApp/[controller]")]
    [Route("KioskApp/[controller]")]
    [ApiController]
    public class SystemController : ControllerBase
    {

        private readonly TBaseLockerController _base;
        private readonly LogController _evento;
        private readonly WebhookService _webhookService;

        /// <summary> -----------------------------------------------------------------------
        /// Constructor
        /// </summary>
        /// <param name="driver"></param>
        /// <param name="context2"></param>
        /// <param name="logger"></param>
        /// <param name="Base"></param>------------------------------------------------------
        public SystemController(TBaseLockerController Base, LogController logController, WebhookService webhookService)
        {
            _base = Base;
            _evento = logController;
            _webhookService = webhookService;
        }

        [HttpGet("GetIP")]
        public SystemNetwork[] GetIP()
        {
            List<SystemNetwork> retorno = new List<SystemNetwork>() { };
            //var ips =Dns.GetHostEntry(Dns.GetHostName());
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {


                if (((item.NetworkInterfaceType == NetworkInterfaceType.Ethernet) ||
                    (item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)) && item.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            SystemNetwork r = new SystemNetwork();
                            r.NetworkInterfaceType = item.NetworkInterfaceType.ToString();
                            r.NetworkOperationalStatus = item.OperationalStatus.ToString();
                            r.IP = ip.Address.ToString();
                            r.NetMask = ip.IPv4Mask.ToString();
                            retorno.Add(r);
                        }
                    }
                }

            }


            return retorno.ToArray();
        }

        [HttpGet("GetSSID")]
        public string[] GetSSID()
        {
            try
            {
                string s0 = cmd("iwconfig wlan0");
                string s1 = cmd("iwlist wlan0 scan | grep ESSID");

                return new string[] { s0, s1 };
                //return new string[] { System.IO.File.ReadAllText("/etc/wpa_supplicant/wpa_supplicant.conf") };
            }
            catch (Exception er)
            {
                return new string[] { "ERROR:" + er.Message };
            }

            /*
            using (Process compiler = new Process())
            {
                compiler.StartInfo.FileName = "nano";
                compiler.StartInfo.Arguments = "/etc/wp";
                compiler.StartInfo.UseShellExecute = false;
                compiler.StartInfo.RedirectStandardOutput = true;
                compiler.Start();

                Console.WriteLine(compiler.StandardOutput.ReadToEnd());

                compiler.WaitForExit();
            }
            */
        }

        [HttpPost("SetSSID")]
        public ActionResult SetSSID([FromBody] SystemSSID ssid)
        {
            string s = "ctrl_interface=DIR=/var/run/wpa_supplicant GROUP=netdev"
                + Environment.NewLine
                + "update_config=1"
                + Environment.NewLine
                + "country=AR" + Environment.NewLine +
                "network={ "
                + Environment.NewLine
                + $"ssid=\"{ssid.SSID}\""
                + Environment.NewLine
                + $"psk=\"{ssid.Pass}\""
                + Environment.NewLine
                + "key_mgmt=WPA-PSK"
                + Environment.NewLine
                + "}";
            try
            {
                System.IO.File.WriteAllText("/etc/wpa_supplicant/wpa_supplicant.conf", s);
                cmd("wpa_supplicant -c /etc/wpa_supplicant/wpa_supplicant.conf -i wlan0");
                return Ok();
            }
            catch (Exception er)
            {
                return BadRequest(er.Message);
            }
        }
        [HttpPost("Filesave")]
        public async Task<ActionResult> PostFile([FromForm] IEnumerable<IFormFile> files)
        {
            var maxAllowedFiles = 3;
            var filesProcessed = 0;
            foreach (var file in files)
            {
                string fileExtension = Path.GetExtension(file.FileName);
                var untrustedFileName = $"tamaños{fileExtension}";

                if (filesProcessed < maxAllowedFiles)
                {

                    try
                    {
                        var path = Path.Combine(_base.PathBase, untrustedFileName);

                        await using FileStream fs = new(path, FileMode.Create);
                        await file.CopyToAsync(fs);



                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine(ex.Message);
                    }


                    filesProcessed++;

                }

            }
            return Ok();
        }

        [HttpPost("SetWLAN")]
        public ActionResult SetWLAN([FromBody] bool state)
        {
            try
            {
                string s0 = cmd("ifconfig wlan0 " + (state ? "up" : "down"));
                return Ok(s0);
            }
            catch (Exception er)
            {
                return BadRequest(er.Message);
            }
        }

        [HttpPost("Reset")]
        public async Task<ActionResult> Reset()
        {
            try
            {
                _evento.AddEvento(new Evento("Reinicio manual del sistema", "sistema"));
                await _webhookService.SendWebhookAsync("Sistema", "Se reinició manualmente el sistema", new { Accion = "Reinicio" });

                string s0 = cmd("reboot");
                return Ok(s0);
            }
            catch (Exception er)
            {
                return BadRequest(er.Message);
            }
        }

        [HttpPost("Shutdown")]
        public async Task<ActionResult> Shutdown()
        {
            try
            {
                _evento.AddEvento(new Evento("Apagado manual del sistema", "sistema"));
                await _webhookService.SendWebhookAsync("Sistema", "Se apagó manualmente el sistema", new { Accion = "Apagado" });

                string s0 = cmd("shutdown -h now");
                return Ok(s0);
            }
            catch (Exception er)
            {
                return BadRequest(er.Message);
            }
        }

        [HttpGet("ResetService")]
        public async Task<string> ResetService()
        {
            try
            {
                _evento.AddEvento(new Evento("Reinicio manual del servicio", "sistema"));
                await _webhookService.SendWebhookAsync("Sistema", "Se reinició manualmente el servicio", new { Accion = "Reinicio servicio" });

                cmd("systemctl restart dcmlocker.service");
                return "Reseteado con éxito";
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return "Error al resetear";
            }
        }

        [HttpGet("TewerID")]
        public async Task<IActionResult> TewerID()
        {
            try
            {
                _evento.AddEvento(new Evento("Se consultó el ID de TeamViewer", "debug"));
                await _webhookService.SendWebhookAsync("Debug", "Se consultó el ID de TeamViewer", new { Accion = "ID TeamViewer" });

                string s0 = cmd("teamviewer daemon start");
                string s1 = cmd("teamviewer info --get-id");
                string pattern = @"\d{10}";

                Match match = Regex.Match(s1, pattern);
                string s2 = "";
                if (match.Success)
                {
                    s2 = match.Value;
                    try  //este try guarda el s2 en el archivo de configuracion
                    {
                        var path = "configjson.json";
                        JsonNode? node;
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            node = JsonNode.Parse(stream);
                        }

                        if (node is not null && int.TryParse(s2, out int tewerID))
                        {
                            node["TewerID"] = tewerID;
                            using (var writeStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                            using (var writer = new StreamWriter(writeStream))
                            {
                                writer.Write(node.ToJsonString(new System.Text.Json.JsonSerializerOptions
                                {
                                    WriteIndented = true
                                }));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error actualizando TewerID: " + ex.Message);
                    }
                    Console.WriteLine("First occurrence of 10 digits: " + s2);
                }
                else
                {
                    Console.WriteLine("No 10 consecutive digits found.");
                }
                return Ok(s2);
            }
            catch (Exception er)
            {
                return BadRequest(er.Message);
            }
        }

        [HttpGet("TewerPASS")]
        public async Task<IActionResult> TewerPASS()
        {
            try
            {
                _evento.AddEvento(new Evento("Se estableció la contraseña preestablecida de TeamViewer", "debug"));
                await _webhookService.SendWebhookAsync("Debug", "Se estableció la contraseña preestablecida de TeamViewer", new { Accion = "Pass TeamViewer" });

                string s0 = cmd("teamviewer daemon start");
                string s1 = cmd("teamviewer passwd lockerinteligente");
                return Ok(s1);
            }
            catch (Exception er)
            {
                return BadRequest(er.Message);
            }
        }

        [HttpPost("Update")]
        public async Task<ActionResult> Update()
        {
            try
            {
                string version = GetVersion();
                _evento.AddEvento(new Evento($"Se actualizó a producción el sistema de la versión {version} a la siguiente", "sistema"));
                await _webhookService.SendWebhookAsync("Sistema", $"Se actualizó a producción el sistema de la versión {version} a la siguiente", new { Accion = "Actualizacion produccion" });

                string s0 = cmd("wget -O - https://raw.githubusercontent.com/DCMSolutions/DCMLockerUpdate/main/update.sh | bash");
                return Ok(s0);
            }
            catch (Exception er)
            {
                return BadRequest(er.Message);
            }
        }

        [HttpPost("UpdateTesting")]
        public async Task<ActionResult> UpdateTesting()
        {
            try
            {
                string version = GetVersion();
                _evento.AddEvento(new Evento($"Se actualizó a testing el sistema de la versión {version} a la siguiente", "sistema"));
                await _webhookService.SendWebhookAsync("Sistema", $"Se actualizó a testing el sistema de la versión {version} a la siguiente", new { Accion = "Actualizacion testing" });

                string s0 = cmd("wget -O - https://raw.githubusercontent.com/DCMSolutions/DCMLockerUpdate/main/updateTesting.sh | bash");
                return Ok(s0);
            }
            catch (Exception er)
            {
                return BadRequest(er.Message);
            }
        }

        private string GetVersion()
        {
            try
            {
                var configPath = "appsettings.json";
                if (!System.IO.File.Exists(configPath))
                {
                    return "";
                }

                var json = System.IO.File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("Version", out JsonElement versionElement))
                {
                    return versionElement.GetString() ?? "";
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        private string cmd(string comando)
        {
            string s = "NO";
            using (Process cmd = new Process())
            {
                cmd.StartInfo.FileName = "/bin/bash";
                cmd.StartInfo.Arguments = $"-c \"sudo {comando}\"";
                cmd.StartInfo.UseShellExecute = false;
                cmd.StartInfo.RedirectStandardOutput = true;
                cmd.Start();
                s = cmd.StandardOutput.ReadToEnd();
                cmd.WaitForExit();
            }
            return s;
        }

        //private void cmdSinSudoNiRta(string comando)
        //{
        //    using (Process cmd = new Process())
        //    {
        //        cmd.StartInfo.FileName = "/bin/bash";
        //        cmd.StartInfo.Arguments = $"-c \"{comando}\"";
        //        cmd.StartInfo.UseShellExecute = false;
        //        cmd.StartInfo.RedirectStandardOutput = false; // No need to capture output
        //        cmd.StartInfo.RedirectStandardError = false;  // Avoid blocking on errors
        //        cmd.Start();
        //    }
        //}

        [HttpGet("GetEthernetConfig")]
        public SystemNetwork GetEthernetConfig()
        {
            SystemNetwork retorno = new SystemNetwork();
            //var ips =Dns.GetHostEntry(Dns.GetHostName());
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {


                if ((item.NetworkInterfaceType == NetworkInterfaceType.Ethernet) && item.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            retorno.NetworkInterfaceType = item.NetworkInterfaceType.ToString();
                            retorno.NetworkOperationalStatus = item.OperationalStatus.ToString();
                            retorno.IP = ip.Address.ToString();
                            retorno.NetMask = ip.IPv4Mask.ToString();
                        }
                    }
                }

            }


            return retorno;
        }

        [HttpPost("SetEthernetIp")]
        public ActionResult SetEthernetIp([FromBody] SystemEthernet eth)
        {


            Console.WriteLine(eth.CalculateGateway());
            string routers = "";
            string s = "interface eth0"
                + Environment.NewLine
                + "metric 303"
                + Environment.NewLine
                + $"static ip_address={eth.IP}" + Environment.NewLine +
                "static routers={routers} "
                + Environment.NewLine
                + $"static domain_name_servers={routers}"
                + Environment.NewLine
                + "interface wlan0"
                + Environment.NewLine
                + "metric 200"
                ;
            try
            {
                //System.IO.File.WriteAllText("/etc/dhcpcd.conf", s);
                //cmd("wpa_supplicant -c /etc/wpa_supplicant/wpa_supplicant.conf -i wlan0");
                return Ok(eth.CalculateGateway());

            }
            catch (Exception er)
            {
                return BadRequest(er.Message);
            }
        }

        /// <summary>
        /// send webhooks
        /// </summary>
        /// <returns></returns>

        [HttpPost("Webhook")]
        public async Task<ActionResult> SendWebhookAsync([FromBody] WebhookRequest request)
        {
            try
            {
                await _webhookService.SendWebhookAsync(request.Evento, request.Descripcion, request.Data);
                return Ok();
            }
            catch (Exception er)
            {
                return BadRequest(er.Message);
            }
        }

        public class WebhookRequest
        {
            public string Evento { get; set; }
            public string Descripcion { get; set; }
            public object Data { get; set; }
        }

        /// <summary>
        /// cosillas de las cerraduuuras
        /// </summary>
        /// <returns></returns>

        private string fileName = Path.Combine(Directory.GetCurrentDirectory(), "cerraduras.ans");

        [HttpGet("cerraduras")]
        public string GetEstadoCerraduras()
        {
            try
            {
                if (!System.IO.File.Exists(fileName))
                {
                    CrearVacia();
                    return "Desconectadas";
                }
                else
                {
                    string content = System.IO.File.ReadAllText(fileName);
                    return content;
                }
            }
            catch
            {
                return "Error";
            }
        }

        [HttpPost("estadocerraduras/{estado}")]
        public bool ChangeEstado(string estado)
        {
            try
            {
                if (!System.IO.File.Exists(fileName))
                {
                    CrearConEstado(estado);
                    return true;
                }
                else
                {
                    Guardar(estado);
                    return true;
                }
            }
            catch
            {
                throw new Exception("No se pudo cambiar el estado de las cerraduras");
            }
        }

        void CrearVacia()
        {
            using StreamWriter sw = System.IO.File.CreateText(fileName);
            sw.Write("Desconectadas");
        }
        void CrearConEstado(string estado)
        {
            using StreamWriter sw = System.IO.File.CreateText(fileName);
            sw.Write(estado);
        }
        void Guardar(string estado)
        {
            System.IO.File.WriteAllText(fileName, estado);
        }

        /// <summary>
        /// cosillas de la config
        /// </summary>
        /// <returns></returns>

        [HttpGet("DelayStatus")]
        public ActionResult<int> GetDelayStatus()
        {
            try
            {
                var path = "configjson.json";

                JsonNode? node;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    node = JsonNode.Parse(stream);
                }

                if (node is null || node["DelayStatus"] is null)
                    return NotFound();

                int delayStatus = node["DelayStatus"]!.GetValue<int>();
                return Ok(delayStatus);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al leer DelayStatus: " + ex.Message);
                return StatusCode(500);
            }
        }

        [HttpGet("SuperadminTime")]
        public ActionResult<int> GetSuperadminTime()
        {
            try
            {
                var path = "configjson.json";

                JsonNode? node;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    node = JsonNode.Parse(stream);
                }

                if (node is null || node["SuperadminTime"] is null)
                    return NotFound();

                int superadminTime = node["SuperadminTime"]!.GetValue<int>();
                return Ok(superadminTime);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al leer SuperadminTime: " + ex.Message);
                return StatusCode(500);
            }
        }

        [HttpGet("AdminTime")]
        public ActionResult<int> GetAdminTime()
        {
            try
            {
                var path = "configjson.json";

                JsonNode? node;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    node = JsonNode.Parse(stream);
                }

                if (node is null || node["AdminTime"] is null)
                    return NotFound();

                int adminTime = node["AdminTime"]!.GetValue<int>();
                return Ok(adminTime);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al leer AdminTime: " + ex.Message);
                return StatusCode(500);
            }
        }

        [HttpPost("DelayStatus")]
        public bool UpdateDelayStatus([FromBody] int delayStatus)
        {
            try
            {
                var path = "configjson.json";

                // Leer y parsear
                JsonNode? node;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    node = JsonNode.Parse(stream);
                }

                if (node is null || node["DelayStatus"] is null)
                    return false;

                // Modificar el valor
                node["DelayStatus"] = delayStatus;

                // Escribir el archivo de vuelta (sobrescribir)
                using (var writeStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(writeStream))
                {
                    writer.Write(node.ToJsonString(new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        [HttpPost("SuperadminTime")]
        public bool UpdateSuperadminTime([FromBody] int superadminTime)
        {
            try
            {
                var path = "configjson.json";

                // Leer y parsear
                JsonNode? node;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    node = JsonNode.Parse(stream);
                }

                if (node is null || node["SuperadminTime"] is null)
                    return false;

                // Modificar el valor
                node["SuperadminTime"] = superadminTime;

                // Escribir el archivo de vuelta (sobrescribir)
                using (var writeStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(writeStream))
                {
                    writer.Write(node.ToJsonString(new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        [HttpPost("AdminTime")]
        public bool UpdateAdminTime([FromBody] int adminTime)
        {
            try
            {
                var path = "configjson.json";

                // Leer y parsear
                JsonNode? node;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    node = JsonNode.Parse(stream);
                }

                if (node is null || node["AdminTime"] is null)
                    return false;

                // Modificar el valor
                node["AdminTime"] = adminTime;

                // Escribir el archivo de vuelta (sobrescribir)
                using (var writeStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(writeStream))
                {
                    writer.Write(node.ToJsonString(new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
