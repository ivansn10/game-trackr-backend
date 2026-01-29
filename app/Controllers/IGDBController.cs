using System.Text;
using System.Text.Json;
using app.repositories;
using app.Services;
using Microsoft.AspNetCore.Mvc;

namespace app.Controllers;

// Define la ruta base para este controlador como "igdb"
[Route("igdb")]
// Marca esta clase como un controlador de API
[ApiController]
public class IGDBController : ControllerBase
{
    // Repositorios y servicios necesarios para las operaciones del controlador
    private readonly GameRepository _gameRepository; // Para acceder a datos de juegos
    private readonly IGDBService _igdbService; // Para consultar la API de IGDB
    private readonly ILogger<IGDBController> _logger; // Para registro de eventos
    private readonly RatingRepository _ratingRepository; // Para acceder a calificaciones de juegos
    private readonly GameStatusRepository _statusRepository; // Para acceder a estados de juegos

    // Constructor que recibe las dependencias mediante inyección
    public IGDBController(
        IGDBService igdbService,
        GameRepository gameRepository,
        RatingRepository ratingRepository,
        GameStatusRepository statusRepository,
        ILogger<IGDBController> logger)
    {
        _igdbService = igdbService;
        _gameRepository = gameRepository;
        _ratingRepository = ratingRepository;
        _statusRepository = statusRepository;
        _logger = logger;
    }

    /// <summary>
    ///     Consulta IGDB directamente por ID de IGDB.
    ///     Ruta completa: GET /igdb/game/{igdbId}
    /// </summary>
    [HttpGet("game/{igdbId}")]
    public async Task<IActionResult> GetGameByIgdbId(int igdbId)
    {
        try
        {
            // Obtener datos directamente de la API de IGDB usando el ID de IGDB
            var result = await _igdbService.GetGameById(igdbId);

            return Ok(JsonDocument.Parse(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener juego {IgdbId} desde IGDB", igdbId);
            // Manejar errores y devolver código 500
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<GameDto>>> SearchGames(
        [FromQuery] string? search,
        [FromQuery] string? genre,
        [FromQuery] string? platform,
        [FromQuery] string? releaseFrom,
        [FromQuery] string? releaseTo,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        try
        {
            var query = new StringBuilder(
            "fields name, summary, cover.image_id, platforms.name, first_release_date, genres.name, category; ");

            var filters = new List<string>
            {
                "cover.image_id != null",
                "version_parent = null",
                "category = (0,4,8,9,10)"
            };

            if (!string.IsNullOrWhiteSpace(genre))
                filters.Add($"genres.name = \"{genre}\"");

            if (!string.IsNullOrWhiteSpace(platform))
                filters.Add($"platforms.name = \"{platform}\"");

            if (long.TryParse(releaseFrom, out var from))
                filters.Add($"first_release_date >= {from}");

            if (long.TryParse(releaseTo, out var to))
                filters.Add($"first_release_date <= {to}");

            if (filters.Count > 0)
                query.Append($"where {string.Join(" & ", filters)}; ");

            if (!string.IsNullOrWhiteSpace(search))
            {
                query.Append($"search \"{search}\"; ");
            }
            else
            {
                query.Append("sort hypes desc; ");
            }

            query.Append($"limit {limit}; offset {offset};");

            var rawJson = await _igdbService.ExecuteQuery(query.ToString());
            _logger.LogDebug("Respuesta de búsqueda IGDB: {Response}", rawJson);

            using var doc = JsonDocument.Parse(rawJson);
            var games = new List<GameDto>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    try
                    {
                        var game = ParseGameDto(item);
                        games.Add(game);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error al procesar juego de la búsqueda");
                    }
                }
            }
            else
            {
                _logger.LogWarning("La respuesta de búsqueda no es un array: {ResponseType}", doc.RootElement.ValueKind);
            }

            return Ok(games);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al buscar juegos");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private GameDto ParseGameDto(JsonElement item)
    {
        var game = new GameDto
        {
            GameId = item.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            IgdbId = item.TryGetProperty("id", out var igdbId) ? igdbId.GetInt32() : 0,
            GameTitle = item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Description = item.TryGetProperty("summary", out var sum) ? sum.GetString() ?? "" : "",
            ReleaseDate = item.TryGetProperty("first_release_date", out var rel)
                ? DateTimeOffset.FromUnixTimeSeconds(rel.GetInt64()).ToString("yyyy-MM-dd")
                : "",
            ImageUrl = item.TryGetProperty("cover", out var cov) &&
                       cov.TryGetProperty("image_id", out var img)
                ? $"https://images.igdb.com/igdb/image/upload/t_cover_big/{img.GetString()}.jpg"
                : "",
            Status = "None",
            Score = null
        };

        // Procesar géneros
        if (item.TryGetProperty("genres", out var genresElement) &&
            genresElement.ValueKind == JsonValueKind.Array)
        {
            game.Genres = new List<string>();
            foreach (var genre in genresElement.EnumerateArray())
                if (genre.TryGetProperty("name", out var genreName))
                    game.Genres.Add(genreName.GetString() ?? "");
        }
        else
        {
            game.Genres = new List<string>();
        }

        // Procesar plataformas
        if (item.TryGetProperty("platforms", out var platformsElement) &&
            platformsElement.ValueKind == JsonValueKind.Array)
        {
            game.Platforms = new List<string>();
            foreach (var platform in platformsElement.EnumerateArray())
                if (platform.TryGetProperty("name", out var platformName))
                    game.Platforms.Add(platformName.GetString() ?? "");
        }
        else
        {
            game.Platforms = new List<string>();
        }

        return game;
    }

    [HttpGet("genres")]
    public async Task<IActionResult> GetGenres()
    {
        try
        {
            var genres = await _igdbService.GetGenres();
            return Ok(genres);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener géneros");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("platforms")]
    public async Task<IActionResult> GetPlatforms()
    {
        try
        {
            var platforms = await _igdbService.GetPlatforms();
            return Ok(platforms);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener plataformas");
            return StatusCode(500, new { error = ex.Message });
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
        public double? Score { get; set; }
    }
}