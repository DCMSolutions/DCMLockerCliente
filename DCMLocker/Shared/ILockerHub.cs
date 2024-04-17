using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DCMLocker.Shared
{
   
    public interface ILockerHub
    {
        Task LockerSet(int OrderID, string message);
        Task LockerUpdated(int OrderID, string message);
        Task Desconexion(bool message);
        Task Conexion(bool message);
        Task AddClientGrupoLocker();
    }
}
