using DCMLocker.Shared;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DCMLocker.Server.Hubs
{
    public class LockerHub : Hub<ILockerHub>
    {
        public async Task SendLockerSetMessage(int OrderID, string Message)
        {
            await Clients.Groups("GP_LOCKER").LockerSet(OrderID, Message);
            //await Clients.All.OrderAdded(OrderID, Message);
        }

        public async Task SendLockerUpdatedMessage(int OrderID, string Message)
        {
            await Clients.Groups("GP_LOCKER").LockerUpdated(OrderID, Message);

            //await Clients.All.OrderUpdated(OrderID, Message);
        }

        public async Task SendDesconexion(bool message)
        {
            await Clients.Groups("GP_LOCKER").Desconexion(message);
        }
        public async Task SendConexion(bool message)
        {
            await Clients.Groups("GP_LOCKER").Conexion(message);
        }

        public async Task AddClientGrupoLocker()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "GP_LOCKER");
        }
        

    }
}
