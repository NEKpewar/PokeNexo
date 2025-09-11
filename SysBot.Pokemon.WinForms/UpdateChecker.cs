using Newtonsoft.Json;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    public class UpdateChecker
    {
        private const string RepositoryOwner = "Daiivr";
        private const string RepositoryName = "PokeNexo";

        private static HttpClient CreateGitHubClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5); // 5 minute timeout for slow connections
            client.DefaultRequestHeaders.Add("User-Agent", "PokeNexo");
            // No auth token needed for public repo
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        public static async Task<(bool UpdateAvailable, bool UpdateRequired, string NewVersion)> CheckForUpdatesAsync(bool forceShow = false)
        {
            try
            {
                ReleaseInfo? latestRelease = await FetchLatestReleaseAsync();
                if (latestRelease == null)
                {
                    if (forceShow)
                    {
                        MessageBox.Show("No se pudo obtener la información de la versión. Por favor, revisa tu conexión a Internet.",
                            "Error al verificar la actualización", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return (false, false, string.Empty);
                }

                bool updateAvailable = latestRelease.TagName != PokeNexo.Version;
                bool updateRequired = !latestRelease.Prerelease && IsUpdateRequired(latestRelease.Body ?? string.Empty);
                string newVersion = latestRelease.TagName ?? string.Empty;

                if (forceShow)
                {
                    var updateForm = new UpdateForm(updateRequired, newVersion, updateAvailable);
                    updateForm.ShowDialog();
                }

                return (updateAvailable, updateRequired, newVersion);
            }
            catch (Exception ex)
            {
                if (forceShow)
                {
                    MessageBox.Show($"Error al buscar actualizaciones: {ex.Message}",
                        "Error al verificar la actualización", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return (false, false, string.Empty);
            }
        }

        public static async Task<string> FetchChangelogAsync()
        {
            try
            {
                ReleaseInfo? latestRelease = await FetchLatestReleaseAsync();
                return latestRelease?.Body ?? "No se pudo obtener la información de la última versión.";
            }
            catch (Exception ex)
            {
                return $"Error al obtener el registro de cambios: {ex.Message}";
            }
        }

        public static async Task<string?> FetchDownloadUrlAsync()
        {
            try
            {
                ReleaseInfo? latestRelease = await FetchLatestReleaseAsync();
                if (latestRelease?.Assets == null || !latestRelease.Assets.Any())
                {
                    Console.WriteLine("No se encontraron recursos en la versión");
                    return null;
                }

                var exeAsset = latestRelease.Assets
                    .FirstOrDefault(a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

                if (exeAsset == null)
                {
                    Console.WriteLine("No se encontró un recurso .exe en la versión");
                    return null;
                }

                // For public repos, use browser_download_url directly
                if (string.IsNullOrEmpty(exeAsset.BrowserDownloadUrl))
                {
                    Console.WriteLine("La URL de descarga está vacía");
                    return null;
                }

                Console.WriteLine($"Se encontró la URL de descarga: {exeAsset.BrowserDownloadUrl}");
                return exeAsset.BrowserDownloadUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al obtener la URL de descarga: {ex.Message}");
                return null;
            }
        }

        private static async Task<ReleaseInfo?> FetchLatestReleaseAsync()
        {
            const int maxRetries = 3;
            Exception? lastException = null;
            
            for (int retry = 0; retry < maxRetries; retry++)
            {
                if (retry > 0)
                {
                    // Wait before retry (exponential backoff)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retry)));
                    Console.WriteLine($"Reintentando intento de descarga {retry + 1}/{maxRetries}...");
                }

                using var client = CreateGitHubClient();
                try
                {
                    string releasesUrl = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
                    Console.WriteLine($"Obteniendo desde la URL: {releasesUrl}");

                    HttpResponseMessage response = await client.GetAsync(releasesUrl);
                    string responseContent = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Error de la API de GitHub: {response.StatusCode} - {responseContent}");
                        lastException = new HttpRequestException($"La API de GitHub devolvió {response.StatusCode}");
                        continue; // Intentar de nuevo
                    }

                    var releaseInfo = JsonConvert.DeserializeObject<ReleaseInfo>(responseContent);
                    if (releaseInfo == null)
                    {
                        Console.WriteLine("No se pudo deserializar la información de la versión");
                        lastException = new InvalidOperationException("No se pudo deserializar la información de la versión");
                        continue; // Intentar de nuevo
                    }

                    Console.WriteLine($"Información de la versión obtenida correctamente. Tag: {releaseInfo.TagName}");
                    return releaseInfo;
                }
                catch (TaskCanceledException ex)
                {
                    Console.WriteLine($"Tiempo de espera agotado en el intento {retry + 1}: {ex.Message}");
                    lastException = ex;
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Error de red en el intento {retry + 1}: {ex.Message}");
                    lastException = ex;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error en el intento {retry + 1}: {ex.Message}");
                    lastException = ex;
                }
            }

            // All retries failed
            Console.WriteLine($"No se pudo obtener la información de la versión después de {maxRetries} intentos");
            if (lastException != null)
                Console.WriteLine($"Último error: {lastException.Message}");

            return null;
        }

        private static bool IsUpdateRequired(string changelogBody)
        {
            return !string.IsNullOrWhiteSpace(changelogBody) &&
                   changelogBody.Contains("Required = Yes", StringComparison.OrdinalIgnoreCase);
        }

        private class ReleaseInfo
        {
            [JsonProperty("tag_name")]
            public string? TagName { get; set; }

            [JsonProperty("prerelease")]
            public bool Prerelease { get; set; }

            [JsonProperty("assets")]
            public List<AssetInfo>? Assets { get; set; }

            [JsonProperty("body")]
            public string? Body { get; set; }
        }

        private class AssetInfo
        {
            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("url")]
            public string? Url { get; set; }

            [JsonProperty("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}
