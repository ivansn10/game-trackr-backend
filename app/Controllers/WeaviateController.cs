using System.Security.Claims;
using app.repositories;
using app.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace app.Controllers;

// Define la ruta base para este controlador como "weaviate"
[Route("weaviate")]
// Marca esta clase como un controlador de API
[ApiController]
public class WeaviateController : ControllerBase
{
    // Repositorios y servicios necesarios para las operaciones del controlador
    private readonly IGDBService _igdbService; // Para consultar la API de IGDB
    private readonly ILogger<WeaviateController> _logger;
    private readonly RatingRepository _ratingRepository; // Para acceder a calificaciones de juegos
    private readonly WeaviateService _weaviateService; // Para interactuar con el servicio de IA Weaviate

    // Constructor que recibe las dependencias mediante inyección
    public WeaviateController(
        WeaviateService weaviateService,
        GameRepository gameRepository,
        RatingRepository ratingRepository,
        GameStatusRepository gameStatusRepository,
        IGDBService igdbService,
        ILogger<WeaviateController> logger)
    {
        _weaviateService = weaviateService;
        _ratingRepository = ratingRepository;
        _igdbService = igdbService;
        _logger = logger;
    }

[HttpGet("recommendations")]
public async Task<IActionResult> GenerateUserRecommendations([FromQuery] string? token = null)
{
    try
    {
        int userId = 0;

        // Determinar el userId desde el token o el usuario autenticado
        if (!string.IsNullOrEmpty(token) && int.TryParse(token, out var parsedTokenId))
        {
            userId = parsedTokenId;
        }
        else
        {
            var userIdClaim = User.FindFirstValue("UserId");
            if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out var parsedUserId))
                userId = parsedUserId;
        }

        try
        {
            var recommendations = await _weaviateService.GetEnhancedRecommendations(userId);

            if (recommendations != null && recommendations.Any())
            {
                var random = new Random();
                var selected = recommendations[random.Next(recommendations.Count)];
                return Ok(selected);
            }

            return Ok(new { message = "No hay recomendaciones disponibles para este usuario." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener recomendaciones de Weaviate");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error general al generar recomendación");
        return StatusCode(500, new { error = ex.Message });
    }
}
}