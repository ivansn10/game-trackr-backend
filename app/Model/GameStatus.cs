namespace app.Model;

/// <summary>
///     Representa el estado de un juego para un usuario (Jugado/Deseado).
/// </summary>
public class GameStatus
{
    /// <summary>
    ///     Identificador único del estado del juego.
    ///     Clave primaria en la base de datos.
    /// </summary>
    public int StatusId { get; set; }

    /// <summary>
    ///     Identificador del usuario asociado al estado del juego.
    ///     Clave foránea que referencia a la tabla Users.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    ///     Identificador del juego cuyo estado se está registrando.
    ///     Clave foránea que referencia a la tabla Games.
    /// </summary>
    public int GameId { get; set; }

    /// <summary>
    ///     Estado del juego: puede ser "Wishlist" (deseo comprarlo), "Owned" (lo tengo),
    ///     "Playing" (lo estoy jugando), "Completed" (lo terminé o ya no lo jugaré más),
    ///     "Abandoned" (decidí no seguir jugándolo).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    ///     Fecha de la última actualización del estado.
    ///     Se inicializa por defecto con la fecha y hora UTC actual.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}