namespace app.Model;

/// <summary>
///     Clase para almacenar el historial de juegos del usuario
/// </summary>
public class UserGameHistory
{
    public int GameId { get; set; }
    public int IgdbId { get; set; }
    public int Rating { get; set; }
    public string? Status { get; set; }
}