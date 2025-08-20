using DCMLocker.Shared.Locker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DCMLocker.Shared
{
    public class ServerStatus
    {
        public string NroSerie { get; set; }
        public List<TLockerMapDTO>? Locker { get; set; }
        public DateTime? LastUpdateTime { get; set; }
        public string? Status { get; set; }
        public int? Empresa { get; set; }
        public string? Version { get; set; }
        public string? IP { get; set; }
        public string? EstadoCerraduras { get; set; }
        public int? DelayStatus { get; set; }
        public int? TewerID { get; set; }
    }

    public class ServerConfig
    {
        public string NroSerie { get; set; }
    }

    public class ServerToken
    {
        public string? NroSerie { get; set; }
        public int? Box { get; set; }
        public string? Token { get; set; }
    }
    public class TLockerMapDTO
    {
        public bool Enable { get; set; }
        public bool Puerta { get; set; }
        public bool Ocupacion { get; set; }
        public int? TempMax { get; set; }
        public int? TempMin { get; set; }
        public int? AlamrNro { get; set; }
        public bool? Libre { get; set; }
        public int? Size { get; set; }
        public int? Id { get; set; }
    }
}
