using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DCMLocker.Server.BaseController;
using DCMLocker.Server.Controllers;
using DCMLocker.Shared;

namespace DCMLocker.Server.Webhooks
{
    public class WebhookService
    {
        private readonly HttpClient _httpClient;
        private readonly TBaseLockerController _base;
        private readonly LogController _evento;

        public WebhookService(HttpClient httpClient , TBaseLockerController Base, LogController evento)
        {
            _httpClient = httpClient;
            _base = Base;
            _evento = evento;
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
                Task.Run(async () =>
                {
                    try
                    {
                        await _httpClient.PostAsJsonAsync(webhookUrl, payload).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error sending webhook: {ex.Message}");
                        _evento.AddEvento(new Evento($"Error inesperado en el webhook: {ex.Message}", "webhook"));
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending webhook: {ex.Message}");
                _ = Task.Run(() => _evento.AddEvento(new Evento($"Error inesperado en el webhook: {ex.Message}", "webhook")));
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
                _ = Task.Run(() => _evento.AddEvento(new Evento($"Error inesperado en el webhook: {ex.Message}", "webhook")));
                return false;
            }
        }
    }
}
