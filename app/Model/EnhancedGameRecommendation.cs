namespace app.Model;

/// <summary>
///     Clase para las recomendaciones mejoradas con información adicional
/// </summary>
public class EnhancedGameRecommendation
{
    public int UserId { get; set; }
    public int GameId { get; set; }
    public string GameTitle { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CoverUrl { get; set; }
    public List<string>? GameGenres { get; set; }
    public List<string>? GamePlatforms { get; set; }
    public float RelevanceScore { get; set; }
    public AdditionalInfo? Additional { get; set; }
}