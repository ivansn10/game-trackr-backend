namespace app.Model;

/// <summary>
///     Representa un videojuego basado en la API de IGDB.
/// </summary>
public class Game
{
    /// <summary>
    ///     Identificador único del juego en la base de datos.
    ///     Este es el ID interno usado en nuestra aplicación.
    /// </summary>
    public int GameId { get; set; }

    /// <summary>
    ///     Identificador del juego en la API de IGDB.
    ///     Este ID se usa para obtener información detallada desde la API externa.
    /// </summary>
    public int IgdbId { get; set; }


    public string GameTitle { get; set; } = "Juego sin título"; // Valor predeterminado
}