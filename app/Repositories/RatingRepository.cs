using app.Data;
using app.Model;
using Dapper;

namespace app.repositories;

/// <summary>
///     Repositorio que maneja operaciones de acceso a datos para la entidad Rating.
/// </summary>
public class RatingRepository
{
    // Conexión a la base de datos
    private readonly Database _database;

    /// <summary>
    ///     Constructor que recibe la dependencia de acceso a datos.
    /// </summary>
    public RatingRepository(Database database)
    {
        _database = database;
    }

    /// <summary>
    ///     Obtiene todas las calificaciones registradas en la base de datos.
    /// </summary>
    /// <returns>Colección de todas las calificaciones.</returns>
    public async Task<IEnumerable<Rating>> GetAll()
    {
        // Creamos una conexión nueva a la base de datos
        using var connection = _database.CreateConnection();
        // Ejecutamos una consulta SQL simple para obtener todas las calificaciones
        return await connection.QueryAsync<Rating>("SELECT * FROM Ratings;");
    }

    /// <summary>
    ///     Obtiene una calificación específica por su ID.
    /// </summary>
    /// <param name="id">ID de la calificación a buscar.</param>
    /// <returns>La calificación encontrada o null si no existe.</returns>
    public async Task<Rating?> GetById(int id)
    {
        using var connection = _database.CreateConnection();
        // Usamos QueryFirstOrDefaultAsync para obtener un solo registro o null
        return await connection.QueryFirstOrDefaultAsync<Rating>(
            "SELECT * FROM Ratings WHERE RatingId = @Id;", new { Id = id }
        );
    }

    /// <summary>
    ///     Agrega una nueva calificación a la base de datos.
    /// </summary>
    /// <param name="rating">Objeto calificación con los datos a insertar.</param>
    public async Task Create(Rating rating)
    {
        using var connection = _database.CreateConnection();
        // Insertar todos los campos de la calificación
        await connection.ExecuteAsync(
            "INSERT INTO Ratings (UserId, GameId, Score, Review, CreatedAt) VALUES (@UserId, @GameId, @Score, @Review, @CreatedAt);",
            rating
        );
    }

    /// <summary>
    ///     Actualiza la información de una calificación existente.
    /// </summary>
    /// <param name="rating">Objeto calificación con los datos actualizados.</param>
    public async Task Update(Rating rating)
    {
        using var connection = _database.CreateConnection();
        // Actualizar los campos de la calificación especificada
        await connection.ExecuteAsync(
            "UPDATE Ratings SET UserId = @UserId, GameId = @GameId, Score = @Score, Review = @Review WHERE RatingId = @RatingId;",
            rating
        );
    }

    /// <summary>
    ///     Elimina una calificación de la base de datos.
    /// </summary>
    /// <param name="id">ID de la calificación a eliminar.</param>
    public async Task Delete(int id)
    {
        using var connection = _database.CreateConnection();
        // Eliminar la calificación con el ID especificado
        await connection.ExecuteAsync("DELETE FROM Ratings WHERE RatingId = @Id;", new { Id = id });
    }
}