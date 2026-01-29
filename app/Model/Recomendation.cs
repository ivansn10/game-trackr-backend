namespace app.Model;

/// <summary>
///     Representa una recomendación de juego basada en IA o preferencias del usuario.
/// </summary>
public class Recommendation
{
    /// <summary>
    ///     Identificador único de la recomendación.
    ///     Clave primaria en la base de datos.
    /// </summary>
    public int RecommendationId { get; set; }

    /// <summary>
    ///     Identificador del usuario al que se le recomienda un juego.
    ///     Clave foránea que referencia a la tabla Users.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    ///     Identificador del juego recomendado.
    ///     Clave foránea que referencia a la tabla Games.
    /// </summary>
    public int GameId { get; set; }

    /// <summary>
    ///     Razón de la recomendación.
    ///     Texto explicativo generado por IA que describe por qué este juego
    ///     podría ser interesante para el usuario según sus preferencias y historial.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    ///     Fecha en la que se generó la recomendación.
    ///     Se inicializa por defecto con la fecha y hora UTC actual.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}