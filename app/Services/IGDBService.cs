using System.Text;
using System.Text.Json;

namespace app.Services;

// Servicio para interactuar con la API de IGDB (base de datos de juegos)
public class IGDBService
{
    // Dependencias necesarias para el funcionamiento del servicio
    private readonly IGDBAuthService _authService; // Servicio para autenticación con IGDB
    private readonly IConfiguration _configuration; // Acceso a la configuración de la aplicación
    private readonly HttpClient _httpClient; // Cliente HTTP para realizar peticiones
    private readonly ILogger<IGDBService> _logger;

    // Constructor que recibe dependencias mediante inyección
    public IGDBService(HttpClient httpClient, IConfiguration configuration, ILogger<IGDBService> logger,
        IGDBAuthService authService)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _authService = authService;
        _logger = logger;
    }

    // Método principal para ejecutar consultas en la API de IGDB
    public async Task<string> ExecuteQuery(string queryBody)
    {
        try
        {
            // Obtener token de acceso para autenticación
            var token = await _authService.GetAccessToken();

            // Obtener Client ID desde la configuración
            var clientId = _configuration["Services:IGDB:ClientId"] ?? _configuration["IGDB:ClientId"];

            // Verificar que el Client ID está configurado
            if (string.IsNullOrEmpty(clientId))
                throw new InvalidOperationException("Client ID de IGDB no configurado correctamente");

            // Configurar encabezados de autorización para la petición
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Client-ID", clientId);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            // Preparar el contenido de la consulta
            var content = new StringContent(queryBody, Encoding.UTF8, "text/plain");

            // Realizar la petición HTTP a la API de IGDB
            var response = await _httpClient.PostAsync("https://api.igdb.com/v4/games", content);

            // Verificar si la petición fue exitosa
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error IGDB: {errorBody}");
                throw new HttpRequestException(
                    $"Error en IGDB: {response.StatusCode} - {response.ReasonPhrase}. Detalles: {errorBody}");
            }

            // Devolver el contenido de la respuesta
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            // Registrar y propagar el error
            Console.WriteLine($"Error en ExecuteQuery: {ex.Message}");
            throw new Exception($"Error en IGDBService: {ex.Message}", ex);
        }
    }

    public async Task<List<string>> GetGenres()
    {
        var token = await _authService.GetAccessToken();
        var clientId = _configuration["Services:IGDB:ClientId"] ?? _configuration["IGDB:ClientId"];

        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("Client ID de IGDB no configurado correctamente");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Client-ID", clientId);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var queryBody = "fields name; limit 500;";
        var content = new StringContent(queryBody, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync("https://api.igdb.com/v4/genres", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Error al obtener géneros: {error}");
            throw new Exception("Error al obtener géneros desde IGDB");
        }

        var json = await response.Content.ReadAsStringAsync();
        var genres = new List<string>();
        using var doc = JsonDocument.Parse(json);
        foreach (var genre in doc.RootElement.EnumerateArray())
            if (genre.TryGetProperty("name", out var nameProp))
                genres.Add(nameProp.GetString() ?? "");

        return genres.Distinct().OrderBy(x => x).ToList();
    }

    public async Task<List<string>> GetPlatforms()
    {
        var token = await _authService.GetAccessToken();
        var clientId = _configuration["Services:IGDB:ClientId"] ?? _configuration["IGDB:ClientId"];

        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("Client ID de IGDB no configurado correctamente");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Client-ID", clientId);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var queryBody = "fields name; limit 500;";
        var content = new StringContent(queryBody, Encoding.UTF8, "text/plain");
        var response = await _httpClient.PostAsync("https://api.igdb.com/v4/platforms", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Error al obtener plataformas: {error}");
            throw new Exception("Error al obtener plataformas desde IGDB");
        }

        var json = await response.Content.ReadAsStringAsync();
        var platforms = new List<string>();
        using var doc = JsonDocument.Parse(json);
        foreach (var platform in doc.RootElement.EnumerateArray())
            if (platform.TryGetProperty("name", out var nameProp))
                platforms.Add(nameProp.GetString() ?? "");

        return platforms.Distinct().OrderBy(x => x).ToList();
    }


    public async Task<string> GetGameById(int gameId)
    {
        try
        {
            var fields = "name, summary, cover.image_id, platforms.name, first_release_date, genres.name";
            var queryBody = $"fields {fields}; where id = {gameId};";

            _logger.LogDebug("Consulta IGDB para juego {GameId}: {Query}", gameId, queryBody);

            var jsonResult = await ExecuteQuery(queryBody);
            _logger.LogDebug("Respuesta IGDB para juego {GameId}: {Response}", gameId, jsonResult);

            // Verificar formato de respuesta y normalizar a un formato estándar
            var normalizedJson = NormalizeResponseFormat(jsonResult, gameId);

            return normalizedJson;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar el juego {GameId}: {Error}", gameId, ex.Message);

            // Devolver un objeto JSON válido en formato estándar para evitar errores
            return $"[{{\"id\":{gameId},\"name\":\"Juego IGDB {gameId}\",\"genres\":[],\"platforms\":[]}}]";
        }
    }

    /// <summary>
    ///     Normaliza el formato de respuesta de IGDB a un formato estándar (siempre array)
    /// </summary>
    private string NormalizeResponseFormat(string jsonResult, int gameId)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResult);
            var root = doc.RootElement;

            // Si ya es un array, mantener el formato
            if (root.ValueKind == JsonValueKind.Array) return jsonResult;

            // Si es un objeto, convertirlo a array con un solo elemento
            if (root.ValueKind == JsonValueKind.Object)
                // Crear un array con un solo elemento
                return $"[{jsonResult}]";

            // Caso inesperado, devolver un objeto vacío en formato array
            _logger.LogWarning("Formato de respuesta inesperado para juego {GameId}: {Kind}",
                gameId, root.ValueKind);

            return $"[{{\"id\":{gameId},\"name\":\"Juego IGDB {gameId}\",\"genres\":[],\"platforms\":[]}}]";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error normalizando respuesta IGDB para juego {GameId}", gameId);
            return $"[{{\"id\":{gameId},\"name\":\"Juego IGDB {gameId}\",\"genres\":[],\"platforms\":[]}}]";
        }
    }


    // Método para obtener juegos lanzados recientemente
    public async Task<string> GetRecentlyReleasedGames(int limit = 10)
    {
        // Definir campos a obtener
        var fields = "name, cover.url";
        // Calcular rango de tiempo (últimos 3 meses)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var threeMonthsAgo = DateTimeOffset.UtcNow.AddMonths(-3).ToUnixTimeSeconds();

        // Construir la consulta: juegos lanzados en los últimos 3 meses, ordenados por fecha
        var queryBody =
            $"fields {fields}; where first_release_date >= {threeMonthsAgo} & first_release_date <= {now}; sort first_release_date desc; limit {limit};";
        return await ExecuteQuery(queryBody);
    }

    // Método para obtener próximos lanzamientos de juegos
    public async Task<string> GetUpcomingGames(int limit = 10)
    {
        // Definir campos a obtener
        var fields = "name, cover.url, first_release_date";
        // Calcular rango de tiempo (próximo año)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var oneYearFromNow = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();

        // Construir la consulta: juegos con lanzamiento en el próximo año, ordenados por fecha
        var queryBody =
            $"fields {fields}; where first_release_date > {now} & first_release_date < {oneYearFromNow}; sort first_release_date asc; limit {limit};";
        return await ExecuteQuery(queryBody);
    }

    // Método para obtener juegos populares según calificación
    public async Task<string> GetPopularGames(int limit = 20)
    {
        // Definir campos a obtener
        var fields = "name, cover.url, rating";

        // Construir la consulta: juegos con alta calificación, ordenados por rating
        var queryBody = $"fields {fields}; where rating > 75; sort rating desc; limit {limit};";
        return await ExecuteQuery(queryBody);
    }

    // Método para buscar juegos por término de búsqueda
    public async Task<string> SearchGames(string searchTerm, int limit = 100)
    {
        // Definir campos a obtener
        var fields = "name, cover.url";

        // Construir la consulta de búsqueda
        var queryBody = $"fields {fields}; search \"{searchTerm}\"; limit {limit};";
        return await ExecuteQuery(queryBody);
    }

    // Método para encontrar juegos similares basados en géneros
    public async Task<string> FindSimilarGames(List<string> genres, int limit = 10)
    {
        try
        {
            // Construir condiciones para filtrar por géneros
            var genreConditions = string.Join(" | ", genres.Select(g => $"genres.name = \"{g}\""));

            // Campos a recuperar
            var fields = "name, cover.url, genres.name";

            // Construir la consulta completa: juegos con géneros coincidentes, ordenados por rating
            var queryBody = $@"
                   fields {fields};
                   where {genreConditions};
                   sort rating desc;
                   limit {limit};
               ";

            // Ejecutar la consulta
            return await ExecuteQuery(queryBody);
        }
        catch (Exception ex)
        {
            // Registrar y propagar el error
            Console.WriteLine($"Error encontrando juegos similares: {ex.Message}");
            throw;
        }
    }


    // Método auxiliar para normalizar nombres de géneros (útil para estandarizar variaciones)
    private string NormalizeGenreName(string genre)
    {
        // Mapeo de nombres de géneros para manejar variaciones
        var genreMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "RPG", "Role-playing (RPG)" },
            { "Action", "Action" },
            { "Adventure", "Adventure" },
            { "Shooter", "Shooter" },
            { "Strategy", "Strategy" },
            { "Sports", "Sports" },
            { "Racing", "Racing" },
            { "Fighting", "Fighting" },
            { "Puzzle", "Puzzle" }
        };

        // Buscar coincidencia, si no se encuentra, devolver el original
        return genreMap.TryGetValue(genre, out var normalizedGenre)
            ? normalizedGenre
            : genre;
    }

    /// <summary>
    ///     Procesa los datos recibidos de IGDB, manejando tanto arrays como objetos
    /// </summary>
    private T ProcessIGDBResponse<T>(string jsonResponse, Func<JsonElement, T> processor)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var rootElement = doc.RootElement;

            // Verificar el tipo de la respuesta
            if (rootElement.ValueKind == JsonValueKind.Array)
            {
                // Es un array, procesar el primer elemento si existe
                var elements = rootElement.EnumerateArray();
                if (elements.Any()) return processor(elements.First());
            }
            else if (rootElement.ValueKind == JsonValueKind.Object)
            {
                // Es un objeto único, procesarlo directamente
                return processor(rootElement);
            }

            // Si no es array ni objeto, o está vacío, lanzar excepción
            throw new JsonException("Formato de respuesta IGDB inesperado");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando respuesta IGDB: {Message}", ex.Message);
            throw;
        }
    }

    public class GameDto
    {
        public int GameId { get; set; }
        public int IgdbId { get; set; }
        public string GameTitle { get; set; } = "";
        public string Description { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public List<string> Genres { get; set; } = new();
        public List<string> Platforms { get; set; } = new();
        public string Status { get; set; } = "None";
        public double? Score { get; set; } = null;
    }
}