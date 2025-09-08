using DCMLocker.Server.Background;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DCMLocker.Server.Hubs
{
    public partial class ServerHub : Hub
    {


        public async Task SendMessage(string user, string message)
        {
            if (Clients != null)
            {
                await Clients.All.SendAsync("ReceiveMessage", user, message);
            }
        }

        public async Task UpdateStatus(string status)
        {
            if (Clients != null)
            {
                await Clients.All.SendAsync("STATUS", status);
            }
        }

        public async Task UpdateCerraduras(string statusCerraduras)
        {
            if (Clients != null)
            {
                await Clients.All.SendAsync("CERRADURAS", statusCerraduras);
            }
        }
    }
}
