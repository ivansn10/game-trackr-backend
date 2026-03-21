using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using app.Data;
using app.Model;
using app.repositories;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using static app.Controllers.IGDBController;

namespace app.Services;

/// <summary>
///     Servicio mejorado para interactuar con Weaviate (motor vectorial de IA)
/// </summary>
public class WeaviateService
{
    // Constantes para gestión de caché
    private const string USER_PROFILE_CACHE_PREFIX = "user_profile_";
    private const string RECOMMENDATIONS_CACHE_PREFIX = "recommendations_user_";
    private const int CACHE_EXPIRATION_MINUTES = 30;

    // Propiedades privadas para dependencias y configuración
    private readonly string _apiUrl;
    private readonly Database? _database;
    private readonly GameRepository _gameRepository;
    private readonly GameStatusRepository? _gameStatusRepository;
    private readonly HttpClient _httpClient;
    private readonly IGDBService _igdbService;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<WeaviateService> _logger;
    private readonly IMemoryCache? _memoryCache;
    private readonly RatingRepository? _ratingRepository;

    // Constructor que recibe dependencias mediante inyección
    public WeaviateService(
        HttpClient httpClient,
        IConfiguration configuration,
        GameRepository gameRepository,
        IGDBService igdbService,
        ILogger<WeaviateService> logger,
        IMemoryCache? memoryCache = null,
        Database? database = null,
        RatingRepository? ratingRepository = null,
        GameStatusRepository? gameStatusRepository = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _gameRepository = gameRepository ?? throw new ArgumentNullException(nameof(gameRepository));
        _igdbService = igdbService ?? throw new ArgumentNullException(nameof(igdbService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _memoryCache = memoryCache;
        _database = database;
        _ratingRepository = ratingRepository;
        _gameStatusRepository = gameStatusRepository;

        // Obtener URL de Weaviate desde configuración con fallback
        _apiUrl = configuration["Weaviate:ApiUrl"] ??
                  configuration["Services:Weaviate:ApiUrl"] ??
                  Environment.GetEnvironmentVariable("Weaviate__ApiUrl") ??
                  "http://weaviate:8080/v1/graphql";

        // Configurar opciones de serialización JSON
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _logger.LogInformation("WeaviateService inicializado con URL: {ApiUrl}", _apiUrl);
    }

    /// <summary>
    ///     Guarda una recomendación en Weaviate
    /// </summary>
    public async Task SaveRecommendation(Recommendation recommendation)
    {
        if (recommendation == null)
            throw new ArgumentNullException(nameof(recommendation));

        try
        {
            _logger.LogDebug("Guardando recomendación para usuario {UserId}, juego {GameId}",
                recommendation.UserId, recommendation.GameId);

            // Extraer información del juego
            var gameInfo = await ExtractGameInfo(recommendation.GameId);

            // Preparar el objeto para enviar a Weaviate con toda la información necesaria
            var weaviateObject = new Dictionary<string, object>
            {
                ["userId"] = recommendation.UserId,
                ["gameId"] = recommendation.GameId,
                ["gameTitle"] = gameInfo.Title,
                ["reason"] = recommendation.Reason,
                ["createdAt"] = recommendation.CreatedAt.ToString("o"),
                ["coverUrl"] = gameInfo.CoverUrl,
                ["uniqueId"] = Guid.NewGuid().ToString()
            };

            // Enviar a Weaviate
            await SendObjectToWeaviate("GameRecommendation", weaviateObject);

            _logger.LogInformation("Recomendación guardada exitosamente para usuario {UserId}, juego {GameId}",
                recommendation.UserId, recommendation.GameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error guardando recomendación: {Message}", ex.Message);
            throw new WeaviateServiceException("Error al guardar recomendación", ex);
        }
    }

    /// <summary>
    ///     Obtiene recomendaciones de Weaviate para un usuario específico
    /// </summary>
    public async Task<List<GameRecommendation>> GetRecommendations(int userId)
    {
        _logger.LogDebug("Obteniendo recomendaciones para usuario {UserId}", userId);

        try
        {

            // Construir consulta GraphQL para Weaviate
            var query = new
            {
                query = $@"
                {{
                    Get {{
                        GameRecommendation(
                            where: {{
                                path: [""userId""],
                                operator: Equal,
                                valueInt: {userId}
                            }},
                            limit: 10,
                            sort: [{{
                                path: [""createdAt""],
                                order: desc
                            }}]
                        ) {{
                            userId
                            gameId
                            gameTitle
                            reason
                            createdAt
                            coverUrl
                        }}
                    }}
                }}"
            };

            // Ejecutar consulta
            var result = await ExecuteWeaviateQuery<WeaviateResponse>(query);

            // Obtener recomendaciones o lista vacía si no hay resultados
            var recommendations = result?.Data?.Get?.GameRecommendation ?? new List<GameRecommendation>();

            // Procesar y enriquecer las recomendaciones con datos adicionales
            var enrichedRecommendations = await ProcessRecommendations(recommendations);

            _logger.LogInformation("Obtenidas {Count} recomendaciones para usuario {UserId}",
                enrichedRecommendations.Count, userId);

            return enrichedRecommendations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo recomendaciones para usuario {UserId}: {Message}",
                userId, ex.Message);
            return new List<GameRecommendation>();
        }
    }

    /// <summary>
    ///     Obtiene recomendaciones mejoradas con manejo mejorado de timeouts de Weaviate
    /// </summary>
 public async Task<List<GameRecommendation>> GetEnhancedRecommendations(int userId)
{
    var startTime = DateTime.UtcNow;

    try
    {
        var weaviateAvailable = await IsWeaviateAvailable();
        if (!weaviateAvailable)
        {
            _logger.LogWarning("Weaviate no está disponible, usando método alternativo para recomendaciones");
            return await GetFallbackRecommendations(userId);
        }

        var cacheKey = $"{RECOMMENDATIONS_CACHE_PREFIX}{userId}";

        if (_memoryCache != null &&
            _memoryCache.TryGetValue(cacheKey, out List<GameRecommendation>? cachedRecommendations) && cachedRecommendations != null)
        {
            _logger.LogDebug("Usando recomendaciones en caché para usuario {UserId}", userId);
            return cachedRecommendations ?? [];
        }

        // 🔧 LLAMADA REAL A WEAVIATE + ENRIQUECIMIENTO
        var recommendations = await GetRecommendations(userId);

        if (_memoryCache != null)
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CACHE_EXPIRATION_MINUTES)
            };
            _memoryCache.Set(cacheKey, recommendations, cacheOptions);
        }

        return recommendations;
    }
    catch (TaskCanceledException ex)
    {
        _logger.LogError(ex, "Timeout al obtener recomendaciones de Weaviate para usuario {UserId}", userId);
        return await GetFallbackRecommendations(userId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error generando recomendaciones mejoradas para usuario {UserId}: {Message}", userId, ex.Message);
        return await GetFallbackRecommendations(userId);
    }
}

    /// <summary>
    ///     Verifica si el servicio Weaviate está disponible y responde correctamente
    /// </summary>
    private async Task<bool> IsWeaviateAvailable()
    {
        try
        {
            // Usar un endpoint ligero para verificar disponibilidad
            var metaUrl = _apiUrl.Replace("/graphql", "/meta");

            // Usar un timeout corto para la verificación
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var response = await _httpClient.GetAsync(metaUrl, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Proporciona recomendaciones alternativas cuando Weaviate no está disponible
    /// </summary>

    private async Task<List<GameRecommendation>> GetFallbackRecommendations(int userId)
    {
        _logger.LogInformation("Generando recomendaciones personalizadas alternativas para usuario {UserId}", userId);

        try
        {
            // 1. Obtener historial de juegos y calificaciones del usuario
            var userRatings = _ratingRepository != null 
                ? await _ratingRepository.GetAll() 
                : new List<Rating>();
            var userStatuses = _gameStatusRepository != null 
                ? await _gameStatusRepository.GetAll() 
                : new List<GameStatus>();

            // Filtrar por usuario
            var filteredRatings = userRatings.Where(r => r.UserId == userId).ToList();
            var filteredStatuses = userStatuses.Where(s => s.UserId == userId).ToList();

            // Lista para almacenar todas las preferencias del usuario
            var userPreferences = new UserPreferenceAnalysis();
            var recommendations = new List<GameRecommendation>();

            // 2. Analizar preferencias del usuario si tiene historial
            if (filteredRatings.Any() || filteredStatuses.Any())
            {
                _logger.LogDebug(
                    "Analizando preferencias del usuario {UserId} basadas en {RatingsCount} calificaciones y {StatusesCount} estados",
                    userId, filteredRatings.Count, filteredStatuses.Count);

                // Obtener preferencias detalladas
                userPreferences = await AnalyzeUserPreferences(userId, filteredRatings, filteredStatuses);

                // Si hay preferencias, buscar juegos similares basados en géneros
                if (userPreferences.FavoriteGenres.Any())
                {
                    var genreBasedRecommendations = await GetGenreBasedRecommendations(
                        userId, userPreferences.FavoriteGenres, userPreferences.PlayedGames);

                    recommendations.AddRange(genreBasedRecommendations);
                }
            }

            // 3. Si no hay suficientes recomendaciones, complementar con juegos populares
            if (recommendations.Count < 5)
            {
                _logger.LogDebug("Complementando con juegos populares para usuario {UserId}", userId);

                // Obtener juegos populares de IGDB
                var additionalCount = 5 - recommendations.Count;
                var popularGamesJson =
                    await _igdbService.GetPopularGames(additionalCount + 3); // Pedir algunos extra por si acaso

                // Procesar respuesta
                var popularRecommendations =
                    await ProcessPopularGames(userId, popularGamesJson, userPreferences.PlayedGames);

                // Añadir solo los necesarios
                recommendations.AddRange(popularRecommendations.Take(additionalCount));
            }

            // 4. Generar razones personalizadas para las recomendaciones
            foreach (var recommendation in recommendations)
                recommendation.Reason = GeneratePersonalizedReason(recommendation, userPreferences);

            return recommendations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando recomendaciones alternativas");
            return await GetBasicRecommendations(userId);
        }
    }

    /// <summary>
    ///     Analiza las preferencias del usuario basadas en calificaciones e historial
    /// </summary>
    private async Task<UserPreferenceAnalysis> AnalyzeUserPreferences(
        int userId,
        List<Rating> ratings,
        List<GameStatus> statuses)
    {
        var result = new UserPreferenceAnalysis();

        try
        {
            // Obtener IDs de juegos que el usuario ha jugado
            var ratedGameIds = ratings.Select(r => r.GameId).ToList();
            var playedGameIds = statuses
                .Where(s => s.Status == "Completed" || s.Status == "Playing")
                .Select(s => s.GameId)
                .ToList();

            // Combinar y eliminar duplicados
            var allPlayedGameIds = ratedGameIds.Union(playedGameIds).Distinct().ToList();
            result.PlayedGames = allPlayedGameIds;

            // Si no hay juegos jugados, retornar análisis vacío
            if (!allPlayedGameIds.Any())
                return result;

            // Obtener detalles de los juegos jugados
            var games = await _gameRepository.GetGamesByIds(allPlayedGameIds);

            // Buscar información de géneros para cada juego
            var genreCounts = new Dictionary<string, int>();
            var platformCounts = new Dictionary<string, int>();

            foreach (var game in games)
                try
                {
                    // Obtener detalles del juego de IGDB
                    var gameDetailsString = await _igdbService.GetGameById(game.IgdbId);
                    using var jsonDoc = JsonDocument.Parse(gameDetailsString);

                    // Extraer géneros y plataformas
                    var genres = ExtractGenres(jsonDoc);
                    var platforms = ExtractPlatforms(jsonDoc);

                    // Obtener rating si existe, o 0 si no
                    var rating = ratings.FirstOrDefault(r => r.GameId == game.GameId)?.Score ?? 0;
                    var weight = rating > 0 ? rating / 10.0f : 0.5f; // Si no hay rating, peso neutral

                    // Acumular preferencias por género con peso
                    foreach (var genre in genres)
                    {
                        if (!genreCounts.ContainsKey(genre))
                            genreCounts[genre] = 0;

                        genreCounts[genre] += (int)(weight * 100); // Convertir a puntos enteros
                    }

                    // Acumular preferencias por plataforma
                    foreach (var platform in platforms)
                    {
                        if (!platformCounts.ContainsKey(platform))
                            platformCounts[platform] = 0;

                        platformCounts[platform] += (int)(weight * 80); // Menos peso que géneros
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error analizando juego {GameId} para preferencias", game.GameId);
                }

            // Obtener géneros y plataformas favoritos (top 3)
            result.FavoriteGenres = genreCounts
                .OrderByDescending(g => g.Value)
                .Take(3)
                .Select(g => g.Key)
                .ToList();

            result.FavoritePlatforms = platformCounts
                .OrderByDescending(p => p.Value)
                .Take(2)
                .Select(p => p.Key)
                .ToList();

            // Calcular calificación promedio
            if (ratings.Any())
            {
                result.AverageRating = (float)ratings.Average(r => r.Score);
            }

            _logger.LogDebug(
                "Análisis de preferencias para usuario {UserId}: Géneros favoritos: {Genres}, Plataformas: {Platforms}",
                userId, string.Join(", ", result.FavoriteGenres), string.Join(", ", result.FavoritePlatforms));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analizando preferencias del usuario {UserId}", userId);
        }

        return result;
    }

private async Task<List<GameRecommendation>> GetGenreBasedRecommendations(
    int userId,
    List<string> favoriteGenres,
    List<int> playedGames)
{
    var recommendations = new List<GameRecommendation>();

    try
    {
        var similarGamesJson = await _igdbService.FindSimilarGames(favoriteGenres, 10);
        using var doc = JsonDocument.Parse(similarGamesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return recommendations;

        foreach (var game in doc.RootElement.EnumerateArray())
        {
            try
            {
                if (!game.TryGetProperty("id", out var idProp) || !game.TryGetProperty("name", out var nameProp))
                    continue;

                var igdbId = idProp.GetInt32();
                var name = nameProp.GetString() ?? string.Empty;

                if (igdbId == 0 || string.IsNullOrEmpty(name))
                    continue;

                var localGame = (await _gameRepository.GetAll()).FirstOrDefault(g => g.IgdbId == igdbId);
                int gameId;

                if (localGame == null)
                {
                    var newGame = new Game { IgdbId = igdbId, GameTitle = name };
                    await _gameRepository.Create(newGame);
                    gameId = newGame.GameId;
                }
                else
                {
                    gameId = localGame.GameId;
                }

                if (playedGames.Contains(gameId))
                    continue;

                var gameDetails = await GetGameDetails(igdbId);
                var description = ExtractDescription(gameDetails);
                var releaseDate = ExtractReleaseDate(gameDetails);

                string coverUrl = ExtractCoverUrl(gameDetails);
                var genres = ExtractGenres(gameDetails);
                var platforms = ExtractPlatforms(gameDetails);

                var gameDto = new GameDto
                {
                    GameId = gameId,
                    IgdbId = igdbId,
                    GameTitle = name,
                    Description = description,
                    ImageUrl = coverUrl,
                    ReleaseDate = releaseDate,
                    Genres = genres,
                    Platforms = platforms,
                    Status = "None",
                    Score = null
                };

                recommendations.Add(new GameRecommendation
                {
                    Game = gameDto,
                    Reason = ""
                });

                if (recommendations.Count >= 5)
                    break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error procesando juego para recomendación basada en géneros");
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error obteniendo recomendaciones basadas en géneros");
    }

    return recommendations;
}



private async Task<List<GameRecommendation>> ProcessPopularGames(
    int userId,
    string popularGamesJson,
    List<int> playedGames)
{
    var recommendations = new List<GameRecommendation>();

    try
    {
        using var doc = JsonDocument.Parse(popularGamesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return recommendations;

        foreach (var game in doc.RootElement.EnumerateArray())
        {
            try
            {
                if (!game.TryGetProperty("id", out var idProp) || idProp.GetInt32() == 0)
                    continue;

                var igdbId = idProp.GetInt32();
                var name = game.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString() ?? $"Juego IGDB {igdbId}"
                    : $"Juego IGDB {igdbId}";

                var localGame = (await _gameRepository.GetAll()).FirstOrDefault(g => g.IgdbId == igdbId);
                int gameId;

                if (localGame == null)
                {
                    var newGame = new Game { IgdbId = igdbId, GameTitle = name };
                    await _gameRepository.Create(newGame);
                    gameId = newGame.GameId;
                }
                else
                {
                    gameId = localGame.GameId;
                }

                if (playedGames.Contains(gameId))
                    continue;

                var gameDetails = await GetGameDetails(igdbId);
                var description = ExtractDescription(gameDetails);
                var releaseDate = ExtractReleaseDate(gameDetails);

                string coverUrl = ExtractCoverUrl(gameDetails);
                var genres = ExtractGenres(gameDetails);
                var platforms = ExtractPlatforms(gameDetails);

                var gameDto = new GameDto
                {
                    GameId = gameId,
                    IgdbId = igdbId,
                    GameTitle = name,
                    Description = description,
                    ImageUrl = coverUrl,
                    ReleaseDate = releaseDate,
                    Genres = genres,
                    Platforms = platforms,
                    Status = "None",
                    Score = null
                };

                recommendations.Add(new GameRecommendation
                {
                    Game = gameDto,
                    Reason = ""
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error procesando juego popular para recomendación");
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error procesando juegos populares");
    }

    return recommendations;
}



/// <summary>
/// Genera una razón personalizada para una recomendación
/// </summary>
private string GeneratePersonalizedReason(GameRecommendation recommendation, UserPreferenceAnalysis preferences)
{
    try
    {
        var game = recommendation.Game;
        var gameGenres = game.Genres ?? new List<string>();

        // Si no hay géneros en la recomendación, usar mensajes genéricos
        if (!gameGenres.Any())
        {
            var genericReasons = new[]
            {
                $"Te recomendamos {game.GameTitle} porque está entre los juegos más populares del momento.",
                $"Juegos como {game.GameTitle} están recibiendo excelentes críticas por parte de la comunidad.",
                $"{game.GameTitle} es un título destacado que podría interesarte basado en tus preferencias.",
                $"Nuestro sistema ha seleccionado {game.GameTitle} como un juego que se alinea con tu perfil de jugador.",
                $"Muchos jugadores con gustos similares a los tuyos están disfrutando de {game.GameTitle}."
            };

            return genericReasons[new Random().Next(genericReasons.Length)];
        }

        // Si hay géneros coincidentes con los favoritos del usuario
        var matchingGenres = gameGenres.Intersect(preferences.FavoriteGenres, StringComparer.OrdinalIgnoreCase).ToList();

        if (matchingGenres.Any())
        {
            var genreBasedReasons = new[]
            {
                $"Basado en tu interés por juegos de {string.Join(", ", matchingGenres)}, creemos que {game.GameTitle} será una excelente adición a tu colección.",
                $"Como aficionado a los juegos de {matchingGenres.First()}, {game.GameTitle} ofrece una experiencia que seguramente disfrutarás.",
                $"{game.GameTitle} combina elementos de {string.Join(" y ", matchingGenres)} que sabemos que te gustan, con un enfoque innovador.",
                $"Tu historial muestra que disfrutas de {string.Join(", ", matchingGenres)}, por lo que {game.GameTitle} debería ser un acierto seguro.",
                $"Nuestra IA ha identificado que {game.GameTitle} comparte temas y mecánicas con tus juegos favoritos de {string.Join(" y ", matchingGenres)}."
            };

            return genreBasedReasons[new Random().Next(genreBasedReasons.Length)];
        }

        // Si no hay coincidencias directas, usar los géneros del juego
        var genreString = string.Join(", ", gameGenres.Take(2));

        var diverseReasons = new[]
        {
            $"Para diversificar tu experiencia, te sugerimos {game.GameTitle}, un juego de {genreString} que podría ampliar tus horizontes.",
            $"Aunque no sueles jugar a títulos de {genreString}, creemos que {game.GameTitle} podría sorprenderte gratamente.",
            $"{game.GameTitle} te ofrece una refrescante experiencia en los géneros de {genreString}, algo diferente a tu biblioteca actual.",
            $"Para probar algo nuevo, {game.GameTitle} combina elementos de {genreString} en una experiencia única.",
            $"Descubre nuevos géneros con {game.GameTitle}, un título aclamado en la categoría de {genreString}."
        };

        return diverseReasons[new Random().Next(diverseReasons.Length)];
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Error generando razón personalizada para {GameTitle}", recommendation.Game?.GameTitle ?? "desconocido");
        return $"Te recomendamos {recommendation.Game?.GameTitle ?? "este juego"} basado en tendencias actuales y tus preferencias.";
    }
}


/// <summary>
/// Devuelve recomendaciones básicas en caso de fallo
/// </summary>
private async Task<List<GameRecommendation>> GetBasicRecommendations(int userId)
{
    var recommendations = new List<GameRecommendation>();

    try
    {
        var popularGamesJson = await _igdbService.GetPopularGames(5);
        using var doc = JsonDocument.Parse(popularGamesJson);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var game in doc.RootElement.EnumerateArray())
            {
                try
                {
                    int igdbId = game.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                    string title = game.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

                    if (igdbId == 0 || string.IsNullOrEmpty(title))
                        continue;

                    var localGame = (await _gameRepository.GetAll())
                        .FirstOrDefault(g => g.IgdbId == igdbId);

                    int gameId;
                    if (localGame == null)
                    {
                        var newGame = new Game { IgdbId = igdbId, GameTitle = title };
                        await _gameRepository.Create(newGame);
                        gameId = newGame.GameId;
                    }
                    else
                    {
                        gameId = localGame.GameId;
                    }

                    var gameDetails = await GetGameDetails(igdbId);
                    var description = ExtractDescription(gameDetails);
                    var releaseDate = ExtractReleaseDate(gameDetails);
                    var coverUrl = ExtractCoverUrl(gameDetails);
                    var genres = ExtractGenres(gameDetails);
                    var platforms = ExtractPlatforms(gameDetails);

                    var gameDto = new GameDto
                    {
                        GameId = gameId,
                        IgdbId = igdbId,
                        GameTitle = title,
                        Description = description,
                        ImageUrl = coverUrl,
                        ReleaseDate = releaseDate,
                        Genres = genres,
                        Platforms = platforms,
                        Status = "None",
                        Score = null
                    };

                    recommendations.Add(new GameRecommendation
                    {
                        Game = gameDto,
                        Reason = "Este juego popular podría ser de tu interés."
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error procesando juego básico");
                }
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error generando recomendaciones básicas");
    }

    return recommendations;
}



/// <summary>
/// Clase para almacenar el análisis de preferencias del usuario
/// </summary>
private class UserPreferenceAnalysis
{
    public List<int> PlayedGames { get; set; } = new();
    public List<string> FavoriteGenres { get; set; } = new();
    public List<string> FavoritePlatforms { get; set; } = new();
    public float AverageRating { get; set; } = 0;
}

    /// <summary>
    ///     Verifica que todas las clases necesarias existen en el esquema de Weaviate
    /// </summary>
    public async Task<bool> VerifyRequiredClasses()
    {
        try
        {
            var schemaEndpoint = _apiUrl.Replace("/graphql", "/schema");
            var response = await _httpClient.GetAsync(schemaEndpoint);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Error al verificar esquema de Weaviate: {StatusCode}", response.StatusCode);
                return false;
            }

            var schemaContent = await response.Content.ReadAsStringAsync();
            var schema = JsonDocument.Parse(schemaContent);

            // Verificar si el esquema tiene la propiedad classes
            if (!schema.RootElement.TryGetProperty("classes", out var classesElement) ||
                classesElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogError("Esquema de Weaviate no contiene clases");
                return false;
            }

            // Lista de clases requeridas
            var requiredClasses = new[]
            {
                "GameRecommendation",
                "UserProfile",
                "UserFeedback",
                "Game",
                "UserRating"
            };

            // Recolectar las clases existentes
            var existingClasses = new List<string>();
            foreach (var classElement in classesElement.EnumerateArray())
                if (classElement.TryGetProperty("class", out var className)) {
                    var classNameStr = className.GetString();
                    if (!string.IsNullOrEmpty(classNameStr))
                    {
                        existingClasses.Add(classNameStr);
                    }
                }

            // Verificar si todas las clases requeridas existen
            var missingClasses = requiredClasses.Except(existingClasses).ToList();
            if (missingClasses.Any())
            {
                _logger.LogError("Faltan clases en el esquema de Weaviate: {MissingClasses}",
                    string.Join(", ", missingClasses));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar clases requeridas en Weaviate");
            return false;
        }
    }


    /// <summary>
    ///     Determina si una excepción es transitoria y se puede reintentar
    /// </summary>
    private bool IsTransientException(Exception ex)
    {
        // Considerar como transitorios problemas de red o ciertos códigos HTTP
        if (ex is HttpRequestException httpEx)
            // Errores 500, 502, 503, 504 son típicamente transitorios
            if (httpEx.Message.Contains("500") ||
                httpEx.Message.Contains("502") ||
                httpEx.Message.Contains("503") ||
                httpEx.Message.Contains("504"))
                return true;

        // Problemas de timeout
        if (ex is TaskCanceledException || ex is TimeoutException) return true;

        return false;
    }


    /// <summary>
    ///     Comprueba el estado de salud del servicio Weaviate
    /// </summary>
    public async Task<object> CheckHealth()
{
    try
    {
        _logger.LogDebug("Verificando estado de salud de Weaviate");

        // 1. Limpiar y preparar la URL de meta
        var metaUrl = _apiUrl.Replace("/graphql", "").TrimEnd('/');
        if (!metaUrl.EndsWith("/meta"))
            metaUrl = $"{metaUrl}/meta";

        var response = await _httpClient.GetAsync(metaUrl);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Servicio Weaviate no disponible. Código: {StatusCode}", response.StatusCode);
            return new { Status = "Degraded", Error = $"Weaviate respondió {response.StatusCode}" };
        }

        var jsonString = await response.Content.ReadAsStringAsync();
        using var result = JsonDocument.Parse(jsonString);
        var root = result.RootElement;

        // 2. Extraer versión de forma segura
        string version = "Unknown";
        if (root.TryGetProperty("version", out var v))
            version = v.GetString() ?? "Unknown";

        // 3. Extraer conteo de esquemas/clases de forma segura
        // Nota: En versiones nuevas esto puede haber cambiado, así que validamos niveles
        int schemaCount = 0;
        if (root.TryGetProperty("hostname", out _) || root.TryGetProperty("modules", out _))
        {
            // Intentamos navegar por el JSON sin que lance excepción
            if (root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("classes", out var classes))
            {
                schemaCount = classes.ValueKind == JsonValueKind.Array ? classes.GetArrayLength() : 0;
            }
        }

        _logger.LogInformation("Servicio Weaviate disponible. Versión: {Version}, Esquemas: {SchemaCount}", version, schemaCount);

        return new
        {
            Status = "Healthy",
            Version = version,
            SchemaCount = schemaCount
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error comprobando estado de Weaviate: {Message}", ex.Message);
        return new
        {
            Status = "Unhealthy",
            Error = ex.Message
        };
    }
}


    /// <summary>
    ///     Envía un objeto a Weaviate
    /// </summary>
    private async Task SendObjectToWeaviate(string className, Dictionary<string, object> properties)
    {
        // Estructura para la consulta usando un diccionario explícito
        var query = new Dictionary<string, object>
        {
            { "class", className }, // Usar "class" en lugar de "class_"
            { "properties", properties }
        };

        // Serializar y enviar
        var jsonQuery = JsonSerializer.Serialize(query, _jsonOptions);
        var content = new StringContent(jsonQuery, Encoding.UTF8, "application/json");

        var objectsUrl = _apiUrl.Replace("/graphql", "");
        if (!objectsUrl.EndsWith("/objects"))
            objectsUrl = $"{objectsUrl}/objects";

        var response = await _httpClient.PostAsync(objectsUrl, content);

        // Verificar respuesta
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Error al enviar objeto a Weaviate. Código: {response.StatusCode}, Detalle: {errorContent}");
        }
    }

    /// <summary>
    ///     Ejecuta una consulta GraphQL en Weaviate con manejo de reintentos mejorado para timeouts
    /// </summary>
    private async Task<T> ExecuteWeaviateQuery<T>(object query)
    {
        var retryCount = 0;
        var maxRetries = 3;
        var initialRetryDelay = TimeSpan.FromSeconds(2);

        while (true)
            try
            {
                var jsonQuery = JsonSerializer.Serialize(query, _jsonOptions);
                var content = new StringContent(jsonQuery, Encoding.UTF8, "application/json");

                // Usar un CancellationToken con timeout específico para este tipo de consultas
                using var
                    cts = new CancellationTokenSource(
                        TimeSpan.FromSeconds(20)); // Reducir el timeout para detectar problemas más rápido

                var response = await _httpClient.PostAsync(_apiUrl, content, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException(
                        $"Error en consulta Weaviate. Código: {response.StatusCode}, Detalle: {errorContent}");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var deserializedObject = JsonSerializer.Deserialize<T>(jsonString, _jsonOptions);
                if (deserializedObject == null)
                {
                    throw new InvalidOperationException("Deserialization resulted in a null object.");
                }
                return deserializedObject;
            }
            catch (TaskCanceledException ex) when (retryCount < maxRetries)
            {
                // Específicamente manejar timeouts
                retryCount++;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // Backoff exponencial

                _logger.LogWarning(ex,
                    "Timeout en consulta a Weaviate, reintentando ({RetryCount}/{MaxRetries}) después de {Delay}s",
                    retryCount, maxRetries, delay.TotalSeconds);

                await Task.Delay(delay);
            }
            catch (Exception ex) when (IsTransientException(ex) && retryCount < maxRetries)
            {
                // Otros errores transitorios
                retryCount++;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // Backoff exponencial

                _logger.LogWarning(ex,
                    "Error transitorio en consulta a Weaviate, reintentando ({RetryCount}/{MaxRetries}) después de {Delay}s",
                    retryCount, maxRetries, delay.TotalSeconds);

                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                // Errores no transitorios o agotado el número de reintentos
                _logger.LogError(ex, "Error permanente en consulta a Weaviate después de {RetryCount} intentos",
                    retryCount);
                throw;
            }
    }

    /// <summary>
    ///     Extrae información básica de un juego
    /// </summary>
    private async Task<(string Title, string CoverUrl)> ExtractGameInfo(int gameId)
    {
        var title = "Juego desconocido";
        var coverUrl = "";

        if (gameId <= 0)
            return (title, coverUrl);

        var localGame = await _gameRepository.GetById(gameId);
        if (localGame == null)
            return (title, coverUrl);

        try
        {
            var gameDetails = await GetGameDetails(localGame.IgdbId);
            title = ExtractGameTitle(gameDetails);
            coverUrl = ExtractCoverUrl(gameDetails);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo obtener información del juego {GameId}: {Message}", gameId, ex.Message);
        }

        return (title, coverUrl);
    }

    /// <summary>
    ///     Obtiene detalles del juego y los parsea
    /// </summary>
    private async Task<JsonDocument> GetGameDetails(int igdbId)
    {
        var igdbGameJson = await _igdbService.GetGameById(igdbId);
        return JsonDocument.Parse(igdbGameJson);
    }

    /// <summary>
    ///     Obtiene información enriquecida de un juego
    /// </summary>
    private async Task<(string Title, string CoverUrl, List<string> Genres, List<string> Platforms, float RelevanceScore
            )>
        GetEnrichedGameInfo(int gameId, int userId)
    {
        var title = "Juego desconocido";
        var coverUrl = "";
        var genres = new List<string>();
        var platforms = new List<string>();
        var relevanceScore = 0.7f; // Valor por defecto

        var localGame = await _gameRepository.GetById(gameId);
        if (localGame == null)
            return (title, coverUrl, genres, platforms, relevanceScore);

        try
        {
            var gameDetails = await GetGameDetails(localGame.IgdbId);

            title = ExtractGameTitle(gameDetails);
            coverUrl = ExtractCoverUrl(gameDetails);
            genres = ExtractGenres(gameDetails);
            platforms = ExtractPlatforms(gameDetails);

            // Calcular relevancia
            relevanceScore = await CalculateRelevanceScore(userId, genres, platforms);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error obteniendo información enriquecida del juego {GameId}: {Message}",
                gameId, ex.Message);
        }

        return (title, coverUrl, genres, platforms, relevanceScore);
    }

/// <summary>
/// Procesa y enriquece recomendaciones básicas
/// </summary>
private async Task<List<GameRecommendation>> ProcessRecommendations(List<GameRecommendation> recommendations)
{
    var uniqueRecommendations = recommendations
        .GroupBy(r => new { r.Game.GameId, r.Reason })
        .Select(g => g.First())
        .ToList();

    var enrichedRecommendations = new List<GameRecommendation>();

    foreach (var rec in uniqueRecommendations)
    {
        try
        {
            if (rec.Game.GameId == 0)
            {
                enrichedRecommendations.Add(new GameRecommendation
                {
                    Game = new GameDto
                    {
                        GameId = 0,
                        IgdbId = 0,
                        GameTitle = "Juego Popular",
                        Description = "",
                        ImageUrl = "",
                        ReleaseDate = "",
                        Genres = new List<string>(),
                        Platforms = new List<string>(),
                        Status = "None",
                        Score = null
                    },
                    Reason = rec.Reason
                });
                continue;
            }

            var localGame = await _gameRepository.GetById(rec.Game.GameId);
            if (localGame == null)
                continue;

            var gameDetails = await GetGameDetails(localGame.IgdbId);
            var title = ExtractGameTitle(gameDetails);
            var imageUrl = ExtractCoverUrl(gameDetails);
            var genres = ExtractGenres(gameDetails);
            var platforms = ExtractPlatforms(gameDetails);
            var description = ExtractDescription(gameDetails);
            var releaseDate = ExtractReleaseDate(gameDetails);


            enrichedRecommendations.Add(new GameRecommendation
            {
                Game = new GameDto
                {
                    GameId = localGame.GameId,
                    IgdbId = localGame.IgdbId,
                    GameTitle = title,
                    Description = description,
                    ImageUrl = imageUrl,
                    ReleaseDate = releaseDate,
                    Genres = genres,
                    Platforms = platforms,
                    Status = "None",
                    Score = null
                },
                Reason = rec.Reason
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error procesando recomendación {GameId}: {Message}", rec.Game.GameId, ex.Message);
        }
    }

    return enrichedRecommendations;
}




    /// <summary>
    ///     Construye un perfil detallado del usuario basado en su historial
    /// </summary>
    private async Task<UserDetailedProfile> BuildDetailedUserProfile(int userId)
    {
        // Verificar caché
        var cacheKey = $"{USER_PROFILE_CACHE_PREFIX}{userId}";

        if (_memoryCache != null && _memoryCache.TryGetValue(cacheKey, out UserDetailedProfile? cachedProfile) && cachedProfile != null)
            return cachedProfile;

        var profile = new UserDetailedProfile();

        try
        {
            // Verificar que tenemos acceso a los datos necesarios
            if (_database == null || _gameStatusRepository == null || _ratingRepository == null)
                return profile;

            // Obtener historial del usuario
            using var connection = _database.CreateConnection();
            var userHistory = await connection.QueryAsync<UserGameHistory>(@"
                WITH UserGames AS (
                    SELECT 
                        g.GameId,
                        g.IgdbId,
                        COALESCE(r.Score, 0) AS Rating,
                        gs.Status
                    FROM Games g
                    LEFT JOIN GameStatuses gs ON g.GameId = gs.GameId AND gs.UserId = @UserId
                    LEFT JOIN Ratings r ON g.GameId = r.GameId AND r.UserId = @UserId
                    WHERE gs.UserId = @UserId OR r.UserId = @UserId
                )
                SELECT * FROM UserGames
            ", new { UserId = userId });

            // Construir lista de juegos jugados
            profile.PlayedGames = userHistory
                .Select(h => h.GameId)
                .Distinct()
                .ToList();

            // Obtener juegos bien calificados (>= 7) para análisis
            var highRatedGames = userHistory
                .Where(h => h.Rating >= 7)
                .Select(h => h.GameId)
                .ToList();

            await PopulateUserPreferences(profile, highRatedGames, userHistory);

            // Calcular métricas adicionales
            CalculateProfileMetrics(profile, userHistory);

            // Guardar en caché
            if (_memoryCache != null)
            {
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromHours(1));

                _memoryCache.Set(cacheKey, profile, cacheOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error construyendo perfil de usuario {UserId}: {Message}",
                userId, ex.Message);
        }

        return profile;
    }

    /// <summary>
    ///     Rellena las preferencias del usuario analizando sus juegos favoritos
    /// </summary>

    private async Task PopulateUserPreferences(
        UserDetailedProfile profile,
        List<int> highRatedGames,
        IEnumerable<UserGameHistory> userHistory)
    {
        var genrePreferences = new Dictionary<string, float>();
        var platformPreferences = new Dictionary<string, float>();

        foreach (var gameId in highRatedGames)
            try
            {
                var game = await _gameRepository.GetById(gameId);
                if (game == null) continue;

                // Obtener detalles del juego
                var gameDetails = await GetGameDetails(game.IgdbId);

                // Extraer información
                var genres = ExtractGenres(gameDetails);
                var platforms = ExtractPlatforms(gameDetails);

                // Peso basado en calificación (0.0-1.0)
                var rating = userHistory.First(h => h.GameId == gameId).Rating;
                var weight = rating / 10.0f;

                // Actualizar preferencias de géneros
                foreach (var genre in genres)
                {
                    if (!genrePreferences.ContainsKey(genre))
                        genrePreferences[genre] = 0;

                    genrePreferences[genre] += weight;
                }

                // Actualizar preferencias de plataformas
                foreach (var platform in platforms)
                {
                    if (!platformPreferences.ContainsKey(platform))
                        platformPreferences[platform] = 0;

                    platformPreferences[platform] += weight * 0.5f; // Menor peso para plataformas
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error analizando juego {GameId} para preferencias: {Message}",
                    gameId, ex.Message);
            }

        // Asignar preferencias
        profile.GenrePreferences = genrePreferences;
        profile.PlatformPreferences = platformPreferences;

        // Obtener tops
        profile.TopGenres = genrePreferences
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => kv.Key)
            .ToList();

        profile.TopPlatforms = platformPreferences
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => kv.Key)
            .ToList();

        // Construir vector de preferencias con nombres sanitizados
        var preferencesVector = new Dictionary<string, float>();

        // Añadir géneros con nombres sanitizados
        foreach (var genre in genrePreferences)
        {
            var sanitizedKey = $"genre_{SanitizePropertyName(genre.Key)}";
            preferencesVector[sanitizedKey] = genre.Value;
        }

        // Añadir plataformas con nombres sanitizados
        foreach (var platform in platformPreferences)
        {
            var sanitizedKey = $"platform_{SanitizePropertyName(platform.Key)}";
            preferencesVector[sanitizedKey] = platform.Value;
        }

        profile.PreferenceVector = preferencesVector;
    }

    /// <summary>
    ///     Sanitiza un nombre de propiedad para cumplir con las restricciones de Weaviate
    /// </summary>
    private string SanitizePropertyName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "_empty";

        // Reemplazar espacios y caracteres especiales con guiones bajos
        var sanitized = Regex.Replace(name, @"[^a-zA-Z0-9]", "_");

        // Asegurar que comienza con letra o guion bajo (requisito de GraphQL)
        if (char.IsDigit(sanitized[0]))
            sanitized = "_" + sanitized;

        return sanitized;
    }

    /// <summary>
    ///     Calcula métricas estadísticas para el perfil de usuario
    /// </summary>
    private void CalculateProfileMetrics(UserDetailedProfile profile, IEnumerable<UserGameHistory> userHistory)
    {
        // Calcular calificación promedio
        var ratedGames = userHistory.Where(h => h.Rating > 0).ToList();
        profile.AverageRating = (float)(ratedGames.Any()
            ? ratedGames.Average(h => h.Rating)
            : 0);
    }


    /// <summary>
    ///     Calcula la puntuación de relevancia para un juego basado en el historial del usuario
    /// </summary>
    private async Task<float> CalculateRelevanceScore(
        int userId,
        List<string> gameGenres,
        List<string> gamePlatforms)
    {
        try
        {
            // Obtener perfil del usuario
            var userProfile = await BuildDetailedUserProfile(userId);

            // Puntuación base
            var baseScore = 0.5f;

            // Ajustar por coincidencia de géneros
            foreach (var genre in gameGenres)
                if (userProfile.GenrePreferences.TryGetValue(genre, out var preference))
                    baseScore += preference * 0.1f;

            // Ajustar por coincidencia de plataformas
            foreach (var platform in gamePlatforms)
                if (userProfile.PlatformPreferences.TryGetValue(platform, out var preference))
                    baseScore += preference * 0.05f;

            // Limitar entre 0 y 1
            return Math.Clamp(baseScore, 0, 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculando relevancia: {Message}", ex.Message);
            return 0.5f; // Valor por defecto
        }
    }

    /// <summary>
    ///     Actualiza el vector de perfil del usuario basado en feedback
    /// </summary>
    private async Task UpdateUserProfileVector(
        int userId,
        List<string> genres,
        List<string> platforms,
        bool isRelevant,
        string feedbackType)
    {
        try
        {
            // Obtener perfil actual
            var userProfile = await BuildDetailedUserProfile(userId);
            var currentPreferences = userProfile.PreferenceVector;

            // Calcular factor de ajuste según tipo de feedback
            var adjustmentFactor = CalculateAdjustmentFactor(isRelevant, feedbackType);

            // Actualizar pesos de géneros
            foreach (var genre in genres)
            {
                var key = $"genre_{genre}";
                if (!currentPreferences.ContainsKey(key))
                    currentPreferences[key] = 0;

                currentPreferences[key] += adjustmentFactor;
                // Limitar valores entre -1 y 1
                currentPreferences[key] = Math.Clamp(currentPreferences[key], -1.0f, 1.0f);
            }

            // Actualizar pesos de plataformas
            foreach (var platform in platforms)
            {
                var key = $"platform_{platform}";
                if (!currentPreferences.ContainsKey(key))
                    currentPreferences[key] = 0;

                currentPreferences[key] += adjustmentFactor * 0.5f; // Menor peso para plataformas
                currentPreferences[key] = Math.Clamp(currentPreferences[key], -1.0f, 1.0f);
            }

            // Guardar perfil actualizado en Weaviate
            await SaveUserProfileVector(userId, currentPreferences);

            // Invalidar caché
            InvalidateUserCache(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error actualizando vector de perfil: {Message}", ex.Message);
        }
    }

    /// <summary>
    ///     Calcula el factor de ajuste basado en tipo de feedback
    /// </summary>
    private float CalculateAdjustmentFactor(bool isRelevant, string feedbackType)
    {
        return feedbackType switch
        {
            "like" => isRelevant ? 0.2f : -0.1f,
            "dislike" => isRelevant ? -0.2f : 0.1f,
            "not_interested" => -0.05f,
            _ => 0f
        };
    }

    /// <summary>
    ///     Almacena el feedback positivo para mejorar recomendaciones futuras
    /// </summary>
    private async Task StorePositiveFeedbackVector(
        int userId,
        int gameId,
        List<string> genres,
        List<string> platforms)
    {
        try
        {

            // Sanitizar géneros y plataformas
            var sanitizedGenres = genres.Select(g => SanitizePropertyName(g)).ToList();
            var sanitizedPlatforms = platforms.Select(p => SanitizePropertyName(p)).ToList();

            // Crear objeto de feedback
            var feedback = new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["gameId"] = gameId,
                ["feedbackType"] = "positive",
                ["gameGenres"] = sanitizedGenres,
                ["gamePlatforms"] = sanitizedPlatforms,
                ["timestamp"] = DateTime.UtcNow.ToString("o")
            };

            // Guardar en Weaviate
            await SendObjectToWeaviate("UserFeedback", feedback);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error guardando feedback positivo: {Message}", ex.Message);
        }
    }


    /// <summary>
    ///     Guarda el vector de preferencias del usuario en Weaviate
    /// </summary>

    private async Task SaveUserProfileVector(int userId, Dictionary<string, float> preferences)
    {
        try
        {

            // Eliminar perfil anterior si existe
            await DeleteUserProfileIfExists(userId);

            // Sanitizar claves del diccionario de preferencias si no lo están ya
            var sanitizedPreferences = new Dictionary<string, float>();
            foreach (var pref in preferences)
                // Verificar si la clave ya está sanitizada
                if (Regex.IsMatch(pref.Key, @"^[_a-zA-Z][_0-9a-zA-Z]*$"))
                {
                    sanitizedPreferences[pref.Key] = pref.Value;
                }
                else
                {
                    // Sanitizar la clave
                    var sanitizedKey = SanitizePropertyName(pref.Key);
                    sanitizedPreferences[sanitizedKey] = pref.Value;
                }

            // Preparar objeto para Weaviate
            var profileObject = new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["preferences"] = sanitizedPreferences,
                ["updatedAt"] = DateTime.UtcNow.ToString("o")
            };

            // Guardar en Weaviate
            await SendObjectToWeaviate("UserProfile", profileObject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error guardando vector de perfil: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    ///     Elimina el perfil de usuario existente en Weaviate
    /// </summary>
    private async Task DeleteUserProfileIfExists(int userId)
    {
        try
        {

            // Buscar perfiles existentes por usuario
            var query = new
            {
                query = $@"
            {{
                Get {{
                    UserProfile(
                        where: {{
                            path: [""userId""],
                            operator: Equal,
                            valueInt: {userId}
                        }}
                    ) {{
                        _additional {{
                            id
                        }}
                    }}
                }}
            }}"
            };

            // Ejecutar consulta
            var result = await ExecuteWeaviateQuery<JsonDocument>(query);

            // Verificar si el resultado contiene los datos esperados
            if (result.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("Get", out var getElement) &&
                getElement.TryGetProperty("UserProfile", out var profilesElement))
            {
                // Verificar que profilesElement sea un array (no nulo)
                if (profilesElement.ValueKind == JsonValueKind.Array)
                {
                    // Procesar perfiles solo si es un array
                    foreach (var profile in profilesElement.EnumerateArray())
                        if (profile.TryGetProperty("_additional", out var additionalElement) &&
                            additionalElement.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            if (string.IsNullOrEmpty(id))
                                continue;

                            var deleteUrl = _apiUrl.Replace("/graphql", $"/objects/UserProfile/{id}");
                            await _httpClient.DeleteAsync(deleteUrl);

                            _logger.LogDebug("Eliminado perfil de usuario {UserId}, ID: {ProfileId}", userId, id);
                        }
                }
                else
                {
                    _logger.LogDebug("No se encontraron perfiles para el usuario {UserId} o el elemento no es un array",
                        userId);
                }
            }
            else
            {
                _logger.LogDebug("Estructura de respuesta inesperada al buscar perfiles de usuario {UserId}", userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error eliminando perfil existente: {Message}", ex.Message);
            // No relanzamos la excepción para permitir que el flujo continúe
        }
    }

    /// <summary>
    ///     Invalida todas las cachés relacionadas con un usuario
    /// </summary>
    private void InvalidateUserCache(int userId)
    {
        if (_memoryCache == null)
            return;

        // Eliminar caché de perfil
        var profileCacheKey = $"{USER_PROFILE_CACHE_PREFIX}{userId}";
        _memoryCache.Remove(profileCacheKey);

        // Eliminar caché de recomendaciones
        var recommendationsCacheKey = $"{RECOMMENDATIONS_CACHE_PREFIX}{userId}";
        _memoryCache.Remove(recommendationsCacheKey);

        _logger.LogDebug("Caché invalidada para usuario {UserId}", userId);
    }

private List<string> ExtractGenres(JsonDocument gameDetails)
{
    var genres = new List<string>();

    try
    {
        JsonElement elementToProcess;

        if (gameDetails.RootElement.ValueKind == JsonValueKind.Array)
        {
            var array = gameDetails.RootElement.EnumerateArray();
            if (!array.Any())
                return genres;

            elementToProcess = array.First();
        }
        else if (gameDetails.RootElement.ValueKind == JsonValueKind.Object)
        {
            elementToProcess = gameDetails.RootElement;
        }
        else
        {
            _logger.LogWarning("Formato no soportado en ExtractGenres: {Kind}", gameDetails.RootElement.ValueKind);
            return genres;
        }

        if (elementToProcess.TryGetProperty("genres", out var genresElement) &&
            genresElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var genre in genresElement.EnumerateArray())
            {
                if (genre.ValueKind == JsonValueKind.Object &&
                    genre.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        genres.Add(name);
                }
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Error extrayendo géneros del juego");
    }

    return genres;
}

    private List<string> ExtractPlatforms(JsonDocument gameDetails)
{
    var platforms = new List<string>();

    try
    {
        JsonElement elementToProcess;

        if (gameDetails.RootElement.ValueKind == JsonValueKind.Array)
        {
            var array = gameDetails.RootElement.EnumerateArray();
            if (!array.Any())
                return platforms;

            elementToProcess = array.First();
        }
        else if (gameDetails.RootElement.ValueKind == JsonValueKind.Object)
        {
            elementToProcess = gameDetails.RootElement;
        }
        else
        {
            _logger.LogWarning("Formato no soportado en ExtractPlatforms: {Kind}", gameDetails.RootElement.ValueKind);
            return platforms;
        }

        if (elementToProcess.TryGetProperty("platforms", out var platformsElement))
        {
            if (platformsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in platformsElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var name = element.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                            platforms.Add(name);
                    }
                    else if (element.ValueKind == JsonValueKind.Object &&
                             element.TryGetProperty("name", out var nameElement))
                    {
                        var name = nameElement.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                            platforms.Add(name);
                    }
                }
            }
            else if (platformsElement.ValueKind == JsonValueKind.String)
            {
                var platformString = platformsElement.GetString();
                if (!string.IsNullOrWhiteSpace(platformString))
                {
                    var split = platformString.Split(',', ';')
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrEmpty(p));

                    platforms.AddRange(split);
                }
            }
        }

        // Fallback: "Platforms" con mayúscula inicial
        if (platforms.Count == 0 &&
            elementToProcess.TryGetProperty("Platforms", out var altPlatformsElement))
        {
            if (altPlatformsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in altPlatformsElement.EnumerateArray())
                {
                    var name = element.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        platforms.Add(name);
                }
            }
            else if (altPlatformsElement.ValueKind == JsonValueKind.String)
            {
                var platformString = altPlatformsElement.GetString();
                if (!string.IsNullOrWhiteSpace(platformString))
                {
                    var split = platformString.Split(',', ';')
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrEmpty(p));

                    platforms.AddRange(split);
                }
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Error extrayendo plataformas del juego");
    }

    return platforms;
}


    /// <summary>
    ///     Extrae el título del juego desde su JSON de IGDB
    /// </summary>
    private string ExtractGameTitle(JsonDocument gameDetails)
    {
        try
        {
            // Determinar el tipo de estructura JSON
            if (gameDetails.RootElement.ValueKind == JsonValueKind.Array)
            {
                var firstGame = gameDetails.RootElement.EnumerateArray().FirstOrDefault();

                if (firstGame.ValueKind != JsonValueKind.Undefined &&
                    firstGame.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString();
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
            }
            else if (gameDetails.RootElement.ValueKind == JsonValueKind.Object)
            {
                // Es un objeto único
                if (gameDetails.RootElement.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString();
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }

                // También probar con "Name" con mayúscula inicial
                if (gameDetails.RootElement.TryGetProperty("Name", out var nameCapElement))
                {
                    var name = nameCapElement.GetString();
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extrayendo título: {Message}", ex.Message);
        }

        return "Juego desconocido";
    }

    /// <summary>
    ///     Extrae la URL de la portada del juego desde su JSON de IGDB
    /// </summary>
private string ExtractCoverUrl(JsonDocument gameDetails)
{
    try
    {
        var firstGame = gameDetails.RootElement.EnumerateArray().FirstOrDefault();

        if (firstGame.ValueKind == JsonValueKind.Undefined)
            return "";

        if (firstGame.TryGetProperty("cover", out var coverElement))
        {
            if (coverElement.TryGetProperty("url", out var urlElement))
            {
                var url = urlElement.GetString();
                if (!string.IsNullOrEmpty(url))
                {
                    if (url.StartsWith("//"))
                        url = "https:" + url;

                    return url.Replace("t_thumb", "t_cover_big");
                }
            }
            else if (coverElement.TryGetProperty("image_id", out var imageIdElement))
            {
                var imageId = imageIdElement.GetString();
                if (!string.IsNullOrEmpty(imageId))
                {
                    return $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";
                }
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Error extrayendo portada del juego");
    }

    return "";
}
    private string ExtractDescription(JsonDocument gameDetails)
    {
        try
        {
            JsonElement elementToProcess;

            if (gameDetails.RootElement.ValueKind == JsonValueKind.Array)
            {
                var array = gameDetails.RootElement.EnumerateArray();
                if (!array.Any())
                    return "";

                elementToProcess = array.First();
            }
            else if (gameDetails.RootElement.ValueKind == JsonValueKind.Object)
            {
                elementToProcess = gameDetails.RootElement;
            }
            else
            {
                _logger.LogWarning("Formato no soportado en ExtractDescription: {Kind}", gameDetails.RootElement.ValueKind);
                return "";
            }

            if (elementToProcess.TryGetProperty("summary", out var summaryElement))
            {
                var description = summaryElement.GetString();
                if (!string.IsNullOrWhiteSpace(description))
                    return description.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extrayendo descripción del juego");
        }

        return "";
    }
    private string ExtractReleaseDate(JsonDocument gameDetails)
    {
        try
        {
            JsonElement elementToProcess;

            if (gameDetails.RootElement.ValueKind == JsonValueKind.Array)
            {
                var array = gameDetails.RootElement.EnumerateArray();
                if (!array.Any())
                    return "";

                elementToProcess = array.First();
            }
            else if (gameDetails.RootElement.ValueKind == JsonValueKind.Object)
            {
                elementToProcess = gameDetails.RootElement;
            }
            else
            {
                _logger.LogWarning("Formato no soportado en ExtractReleaseDate: {Kind}", gameDetails.RootElement.ValueKind);
                return "";
            }

            if (elementToProcess.TryGetProperty("first_release_date", out var releaseDateElement) &&
                releaseDateElement.ValueKind == JsonValueKind.Number)
            {
                var timestamp = releaseDateElement.GetInt64();
                var releaseDate = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
                return releaseDate.ToString("yyyy-MM-dd");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extrayendo fecha de lanzamiento del juego");
        }

        return "";
    }

    // Gestión de calificaciones de usuarios

    /// <summary>
    ///     Almacena una calificación de usuario en Weaviate
    /// </summary>
    public async Task StoreUserRating(Rating rating)
    {
        if (rating == null)
            throw new ArgumentNullException(nameof(rating));

        try
        {
            _logger.LogDebug("Almacenando calificación para usuario {UserId}, juego {GameId}, puntuación {Score}",
                rating.UserId, rating.GameId, rating.Score);

            // Obtener información del juego
            var game = await _gameRepository.GetById(rating.GameId);
            if (game == null)
            {
                _logger.LogWarning("No se pudo encontrar el juego con ID {GameId}", rating.GameId);
                return;
            }

            // Obtener detalles del juego desde IGDB
            var gameDetails = await GetGameDetails(game.IgdbId);
            var genres = ExtractGenres(gameDetails);
            var platforms = ExtractPlatforms(gameDetails);

            // Preparar objeto para Weaviate
            var ratingObject = new Dictionary<string, object>
            {
                ["userId"] = rating.UserId,
                ["gameId"] = rating.GameId,
                ["score"] = rating.Score,
                ["gameGenres"] = genres,
                ["gamePlatforms"] = platforms,
                ["createdAt"] = rating.CreatedAt.ToString("o"),
                ["uniqueId"] = Guid.NewGuid().ToString()
            };

            // Enviar a Weaviate
            await SendObjectToWeaviate("UserRating", ratingObject);

            // Actualizar vector de perfil del usuario
            await UpdateUserProfileVector(
                rating.UserId,
                genres,
                platforms,
                rating.Score >= 7, // Considerar calificaciones >= 7 como positivas
                "rating"
            );

            // Invalidar caché de recomendaciones
            InvalidateUserCache(rating.UserId);

            _logger.LogInformation(
                "Calificación almacenada exitosamente: Usuario {UserId}, Juego {GameId}, Puntuación {Score}",
                rating.UserId, rating.GameId, rating.Score);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error almacenando calificación: {Message}", ex.Message);
            throw new WeaviateServiceException("Error al almacenar calificación de usuario", ex);
        }
    }

    /// <summary>
    ///     Actualiza una calificación existente de usuario en Weaviate
    /// </summary>
    public async Task UpdateUserRating(Rating rating)
    {
        if (rating == null)
            throw new ArgumentNullException(nameof(rating));

        try
        {
            _logger.LogDebug("Actualizando calificación para usuario {UserId}, juego {GameId}, puntuación {Score}",
                rating.UserId, rating.GameId, rating.Score);

            // Eliminar calificación anterior
            await RemoveUserRating(rating);

            // Almacenar nueva calificación
            await StoreUserRating(rating);

            _logger.LogInformation(
                "Calificación actualizada exitosamente: Usuario {UserId}, Juego {GameId}, Puntuación {Score}",
                rating.UserId, rating.GameId, rating.Score);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error actualizando calificación: {Message}", ex.Message);
            throw new WeaviateServiceException("Error al actualizar calificación de usuario", ex);
        }
    }

    /// <summary>
    ///     Elimina una calificación de usuario de Weaviate
    /// </summary>
    public async Task RemoveUserRating(Rating existingRating)
    {
        if (existingRating == null)
            throw new ArgumentNullException(nameof(existingRating));

        try
        {
            _logger.LogDebug("Eliminando calificación para usuario {UserId}, juego {GameId}",
                existingRating.UserId, existingRating.GameId);

            // Construir consulta para encontrar la calificación
            var query = new
            {
                query = $@"
            {{
                Get {{
                    UserRating(
                        where: {{
                            operator: And,
                            operands: [
                                {{
                                    path: [""userId""],
                                    operator: Equal,
                                    valueInt: {existingRating.UserId}
                                }},
                                {{
                                    path: [""gameId""],
                                    operator: Equal,
                                    valueInt: {existingRating.GameId}
                                }}
                            ]
                        }}
                    ) {{
                        _additional {{
                            id
                        }}
                    }}
                }}
            }}"
            };

            // Ejecutar consulta
            var result = await ExecuteWeaviateQuery<JsonDocument>(query);

            // Procesar resultado para buscar IDs
            if (result.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("Get", out var getElement) &&
                getElement.TryGetProperty("UserRating", out var ratingsElement))
                // Eliminar cada calificación encontrada
                foreach (var rating in ratingsElement.EnumerateArray())
                    if (rating.TryGetProperty("_additional", out var additionalElement) &&
                        additionalElement.TryGetProperty("id", out var idElement))
                    {
                        var id = idElement.GetString();
                        if (string.IsNullOrEmpty(id))
                            continue;

                        var deleteUrl = _apiUrl.Replace("/graphql", $"/objects/UserRating/{id}");
                        await _httpClient.DeleteAsync(deleteUrl);

                        _logger.LogDebug("Eliminada calificación: Usuario {UserId}, Juego {GameId}, ID: {RatingId}",
                            existingRating.UserId, existingRating.GameId, id);
                    }

            // Invalidar caché de recomendaciones
            InvalidateUserCache(existingRating.UserId);

            _logger.LogInformation("Calificación eliminada exitosamente: Usuario {UserId}, Juego {GameId}",
                existingRating.UserId, existingRating.GameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando calificación: {Message}", ex.Message);
            // Loguear el error pero no interrumpir el flujo principal
        }
    }


    /// <summary>
    ///     Almacena un juego con sus detalles completos en Weaviate
    /// </summary>
    public async Task StoreGameWithDetails(Game game)
    {
        if (game == null)
            throw new ArgumentNullException(nameof(game));

        try
        {
            _logger.LogDebug("Almacenando juego {GameId} (IGDB: {IgdbId}) en Weaviate",
                game.GameId, game.IgdbId);

            // Obtener detalles del juego desde IGDB
            var gameDetails = await GetGameDetails(game.IgdbId);

            // Extraer información relevante
            var title = ExtractGameTitle(gameDetails);
            var coverUrl = ExtractCoverUrl(gameDetails);
            var genres = ExtractGenres(gameDetails);
            var platforms = ExtractPlatforms(gameDetails);

            // Sanitizar géneros y plataformas
            var sanitizedGenres = genres.Select(g => SanitizePropertyName(g)).ToList();
            var sanitizedPlatforms = platforms.Select(p => SanitizePropertyName(p)).ToList();

            var releaseDateStr = ExtractReleaseDate(gameDetails);
            DateTime? releaseDate = null;

            if (DateTime.TryParse(releaseDateStr, out var parsedDate))
                releaseDate = parsedDate;

            // Preparar objeto para Weaviate
            var gameObject = new Dictionary<string, object>
            {
                ["gameId"] = game.GameId,
                ["igdbId"] = game.IgdbId,
                ["title"] = title,
                ["coverUrl"] = coverUrl,
                ["gameGenres"] = sanitizedGenres,
                ["gamePlatforms"] = sanitizedPlatforms,
                ["indexedAt"] = DateTime.UtcNow.ToString("o")
            };

            // Añadir fecha de lanzamiento si está disponible
            if (releaseDate.HasValue) gameObject["releaseDate"] = releaseDate.Value.ToString("o");

            // Enviar a Weaviate
            await SendObjectToWeaviate("Game", gameObject);

            _logger.LogInformation("Juego almacenado exitosamente: {GameId} (IGDB: {IgdbId}), Título: {Title}",
                game.GameId, game.IgdbId, title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error almacenando juego: {Message}", ex.Message);
            throw new WeaviateServiceException("Error al almacenar juego con detalles", ex);
        }
    }


// Clase para estadísticas del servicio de recomendaciones
    public class RecommendationServiceStats
    {
        public int CachedUsers { get; set; }
        public int CachedRecommendations { get; set; }
        public int TotalApiCalls { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public int CurrentActiveRequests { get; set; }

        public Dictionary<string, int> ApiCallsByOperation { get; set; } = new();

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}

/// <summary>
///     Excepción específica para errores del servicio de Weaviate
/// </summary>
public class WeaviateServiceException : Exception
{
    public WeaviateServiceException(string message) : base(message)
    {
    }

    public WeaviateServiceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}