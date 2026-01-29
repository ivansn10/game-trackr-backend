namespace app.Model;

/// <summary>
///     Representa una calificación otorgada por un usuario a un juego.
/// </summary>
public class Rating
{
    /// <summary>
    ///     Identificador único de la calificación.
    ///     Clave primaria en la base de datos.
    /// </summary>
    public int RatingId { get; set; }

    /// <summary>
    ///     Identificador del usuario que realizó la calificación.
    ///     Clave foránea que referencia a la tabla Users.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    ///     Identificador del juego calificado.
    ///     Clave foránea que referencia a la tabla Games.
    /// </summary>
    public int GameId { get; set; }

    /// <summary>
    ///     Puntuación otorgada al juego (escala de 1 a 10).
    ///     En la base de datos tiene una restricción CHECK para valores entre 1 y 10.
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    ///     Fecha en la que se realizó la calificación.
    ///     Se inicializa por defecto con la fecha y hora UTC actual.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}