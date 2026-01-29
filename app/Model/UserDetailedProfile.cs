namespace app.Model;

/// <summary>
///     Clase para almacenar el perfil detallado del usuario
/// </summary>
public class UserDetailedProfile
{
    public List<int> PlayedGames { get; set; } = new();
    public List<string> TopGenres { get; set; } = new();
    public List<string> TopPlatforms { get; set; } = new();
    public Dictionary<string, float> PreferenceVector { get; set; } = new();
    public Dictionary<string, float> GenrePreferences { get; set; } = new();
    public Dictionary<string, float> PlatformPreferences { get; set; } = new();
    public float AverageRating { get; set; }
    public float AveragePlaytime { get; set; }
}