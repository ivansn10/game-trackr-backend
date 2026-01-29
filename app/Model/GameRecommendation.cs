using static app.Controllers.IGDBController;

namespace app.Model;

/// <summary>
///     Clase que representa una recomendación de juego
/// </summary>
public class GameRecommendation
{
    public GameDto Game { get; set; } = default!;
    public string Reason { get; set; } = string.Empty;
} 