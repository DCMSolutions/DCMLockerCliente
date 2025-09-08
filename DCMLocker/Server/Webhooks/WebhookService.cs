using DCMLocker.Server.BaseController;
using DCMLocker.Server.Controllers;
using DCMLocker.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace DCMLocker.Server.Webhooks
{
    public class WebhookService
    {
        private readonly HttpClient _httpClient;
        private readonly TBaseLockerController _base;
        private readonly IServiceScopeFactory _scopeFactory;

        public WebhookService(HttpClient httpClient, TBaseLockerController Base, IServiceScopeFactory scopeFactory)
        {
            _httpClient = httpClient;
            _base = Base;
            _scopeFactory = scopeFactory;
        }

        private async Task LogAsync(Evento ev)
        {
            using var scope = _scopeFactory.CreateScope();
            var log = scope.ServiceProvider.GetRequiredService<LogController>();
            await log.AddEvento(ev);
        }

        public bool SendWebhook(string evento, string descripcion, object data)
        {
            try
            {
                var config = _base.Config;
                if (config?.UrlServer == null)
                {
                    Console.WriteLine("Fallo en la URL server.");
                    return false;
                }

                Uri webhookUrl = new Uri(config.UrlServer, "api/WebhookLocker");

                var payload = new Webhook(evento, config.LockerID, descripcion, data);

                // mando el post en otro lado para que no tenga que ser awaited
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _httpClient.PostAsJsonAsync(webhookUrl, payload).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error sending webhook: {ex.Message}");
                        await LogAsync(new Evento($"Error inesperado en el webhook: {ex.Message}", "webhook")).ConfigureAwait(false);
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending webhook: {ex.Message}");
                _ = Task.Run(() => LogAsync(new Evento($"Error inesperado en el webhook: {ex.Message}", "webhook")));
                return false;
            }
        }

        public async Task<bool> SendWebhookAsync(string evento, string descripcion, object data)
        {
            try
            {
                var config = _base.Config;
                if (config?.UrlServer == null)
                {
                    Console.WriteLine("Fallo en la URL server.");
                    return false;
                }

                Uri webhookUrl = new Uri(config.UrlServer, "api/WebhookLocker");

                var payload = new Webhook(evento, config.LockerID, descripcion, data);

                // post es awaited
                var response = await _httpClient.PostAsJsonAsync(webhookUrl, payload);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending webhook: {ex.Message}");
                await LogAsync(new Evento($"Error inesperado en el webhook: {ex.Message}", "webhook"));
                return false;
            }
        }
    }
}
