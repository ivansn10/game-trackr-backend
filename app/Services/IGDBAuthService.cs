using System.Text.Json;

namespace app.Services;

/// <summary>
///     Servicio para autenticación en IGDB.
/// </summary>
public class IGDBAuthService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private DateTime _tokenExpiration = DateTime.MinValue;

    public IGDBAuthService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    /// <summary>
    ///     Obtiene el token de acceso de IGDB. Si ya está en memoria y no ha expirado, lo reutiliza.
    /// </summary>
    public async Task<string> GetAccessToken()
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiration) return _accessToken;

        // Intenta ambas rutas posibles para las credenciales
        var clientId = _configuration["Services:IGDB:ClientId"] ?? _configuration["IGDB:ClientId"];
        var clientSecret = _configuration["Services:IGDB:ClientSecret"] ?? _configuration["IGDB:ClientSecret"];

        Console.WriteLine($"ClientID: {clientId?.Substring(0, 4)}... Secret: {clientSecret?.Substring(0, 4)}...");

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            throw new InvalidOperationException("Las credenciales de IGDB no están configuradas correctamente.");

        // Crear el contenido de la solicitud en formato x-www-form-urlencoded
        var requestData = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "grant_type", "client_credentials" }
        };

        var requestContent = new FormUrlEncodedContent(requestData);

        try
        {
            var response = await _httpClient.PostAsync("https://id.twitch.tv/oauth2/token", requestContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error en token: {errorBody}");
                throw new HttpRequestException(
                    $"Error obteniendo el token de acceso de IGDB: {response.StatusCode} - {response.ReasonPhrase}. Detalles: {errorBody}");
            }

            var responseData = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Token response: {responseData}");
            var json = JsonSerializer.Deserialize<JsonElement>(responseData);

            _accessToken = json.GetProperty("access_token").GetString() ??
                           throw new Exception("No se recibió un token válido.");
            var expiresIn = json.GetProperty("expires_in").GetInt32();
            _tokenExpiration = DateTime.UtcNow.AddSeconds(expiresIn);

            return _accessToken;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en GetAccessToken: {ex.Message}");
            throw new Exception($"Error en IGDBAuthService: {ex.Message}", ex);
        }
    }
}