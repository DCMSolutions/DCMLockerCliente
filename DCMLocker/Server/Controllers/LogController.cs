using DCMLocker.Shared;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DCMLocker.Server.Controllers
{
    [Route("ClientApp/[controller]")]
    [Route("KioskApp/[controller]")]
    [ApiController]
    public class LogController : ControllerBase
    {
        [HttpGet]
        public async Task<List<Evento>> GetEventos()
        {
            string fileNameAhora = Path.Combine("/home/pi", $"eventos-{DateTime.Now:MM-yyyy}.ans");
            try
            {
                if (!System.IO.File.Exists(fileNameAhora))
                {
                    CrearVacia();
                    return new List<Evento>();
                }

                var eventos = await LeerEventosDesdeArchivo(fileNameAhora);
                eventos.Reverse();
                return eventos;
            }
            catch
            {
                throw new Exception("Hubo un error al buscar los eventos");
            }
        }

        [HttpGet("viejo")]
        public async Task<List<Evento>> GetEventosViejos(int mesesAtras)
        {
            string fileNameVieja = Path.Combine("/home/pi", $"eventos-{DateTime.Now.AddMonths(-mesesAtras):MM-yyyy}.ans");

            try
            {
                if (!System.IO.File.Exists(fileNameVieja))
                {
                    return new List<Evento>();
                }

                var eventos = await LeerEventosDesdeArchivo(fileNameVieja);
                eventos.Reverse();
                return eventos;
            }
            catch
            {
                throw new Exception($"Hubo un error al buscar los eventos del {DateTime.Now.AddMonths(-mesesAtras):MM de yyyy}");
            }
        }

        [HttpPost]
        public async Task<bool> AddEvento([FromBody] Evento evento)
        {
            string fileNameAhora = Path.Combine("/home/pi", $"eventos-{DateTime.Now:MM-yyyy}.ans");
            try
            {
                List<Evento> eventos;
                if (!System.IO.File.Exists(fileNameAhora))
                {
                    eventos = new List<Evento>();
                }
                else
                {
                    eventos = await LeerEventosDesdeArchivo(fileNameAhora);
                }

                eventos.Add(evento);
                Guardar(eventos);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool DeleteAllEventos()
        {
            try
            {
                CrearVacia();
                return true;
            }
            catch
            {
                throw new Exception("No se pudieron eliminar los eventos");
            }
        }

        //funciones auxiliares
        void CrearVacia()
        {
            string fileNameAhora = Path.Combine("/home/pi", $"eventos-{DateTime.Now:MM-yyyy}.ans");
            List<Evento> nuevaLista = new();
            string json = JsonSerializer.Serialize(nuevaLista, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(fileNameAhora, json);
        }

        void Guardar(List<Evento> eventos)
        {
            string fileNameAhora = Path.Combine("/home/pi", $"eventos-{DateTime.Now:MM-yyyy}.ans");
            string json = JsonSerializer.Serialize(eventos, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(fileNameAhora, json);
        }

        //async Task<List<Evento>> LeerEventosDesdeArchivo(string fileName)
        //{
        //    try
        //    {
        //        using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        //        var eventos = await JsonSerializer.DeserializeAsync<List<Evento>>(fs);
        //        return eventos ?? new();
        //    }
        //    catch (JsonException)
        //    {
        //        return new();
        //    }
        //    catch (IOException)
        //    {
        //        return new();
        //    }
        //}
        
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = false
        };

        public async Task<List<Evento>> LeerEventosDesdeArchivo(string fileName)
        {
            try
            {
                using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var eventos = await JsonSerializer.DeserializeAsync<List<Evento>>(fs, _jsonOpts);
                return eventos ?? new();
            }
            catch (JsonException)
            {
                var nuevos = new List<Evento> { new Evento("se corrompio el viejo", "error") };

                try
                {
                    var dir = Path.GetDirectoryName(fileName) ?? ".";
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                    var backup = Path.Combine(dir, $"{name}.corrupt-{stamp}{ext}");

                    // Renombrar el archivo corrupto
                    if (System.IO.File.Exists(fileName))
                        System.IO.File.Move(fileName, backup); // .NET 5: sin overwrite

                    // Escribir nuevo archivo con un solo evento (movida “atómica” simple)
                    var tmp = Path.Combine(dir, $"{name}.{Guid.NewGuid():N}.tmp");
                    await System.IO.File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(nuevos, _jsonOpts));
                    if (System.IO.File.Exists(fileName)) System.IO.File.Delete(fileName);
                    System.IO.File.Move(tmp, fileName);
                }
                catch
                {
                    // Si algo falla al resguardar/crear, devolvemos igual la lista en memoria
                }

                return nuevos;
            }
            catch (IOException)
            {
                return new();
            }
        }


    }
}
