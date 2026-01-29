using app.Data;
using app.Model;
using Dapper;

namespace app.repositories;

/// <summary>
///     Repositorio que maneja operaciones de acceso a datos para la entidad Recommendation.
/// </summary>
public class RecommendationRepository
{
    // Conexión a la base de datos
    private readonly Database _database;

    /// <summary>
    ///     Constructor que recibe la dependencia de acceso a datos.
    /// </summary>
    public RecommendationRepository(Database database)
    {
        _database = database;
    }

    /// <summary>
    ///     Obtiene todas las recomendaciones registradas en la base de datos.
    /// </summary>
    /// <returns>Colección de todas las recomendaciones.</returns>
    public async Task<IEnumerable<Recommendation>> GetAll()
    {
        // Creamos una conexión nueva a la base de datos
        using var connection = _database.CreateConnection();
        // Ejecutamos una consulta SQL simple para obtener todas las recomendaciones
        return await connection.QueryAsync<Recommendation>("SELECT * FROM Recommendations;");
    }

    /// <summary>
    ///     Obtiene una recomendación específica por su ID.
    /// </summary>
    /// <param name="id">ID de la recomendación a buscar.</param>
    /// <returns>La recomendación encontrada o null si no existe.</returns>
    public async Task<Recommendation?> GetById(int id)
    {
        using var connection = _database.CreateConnection();
        // Usamos QueryFirstOrDefaultAsync para obtener un solo registro o null
        return await connection.QueryFirstOrDefaultAsync<Recommendation>(
            "SELECT * FROM Recommendations WHERE RecommendationId = @Id;", new { Id = id }
        );
    }

    /// <summary>
    ///     Agrega una nueva recomendación a la base de datos.
    /// </summary>
    /// <param name="recommendation">Objeto recomendación con los datos a insertar.</param>
    public async Task Create(Recommendation recommendation)
    {
        using var connection = _database.CreateConnection();
        // Insertar los campos de la recomendación
        await connection.ExecuteAsync(
            "INSERT INTO Recommendations (UserId, GameId, Reason, CreatedAt) VALUES (@UserId, @GameId, @Reason, @CreatedAt);",
            recommendation
        );
    }

    /// <summary>
    ///     Actualiza la información de una recomendación existente.
    /// </summary>
    /// <param name="recommendation">Objeto recomendación con los datos actualizados.</param>
    public async Task Update(Recommendation recommendation)
    {
        using var connection = _database.CreateConnection();
        // Actualizar los campos de la recomendación especificada
        await connection.ExecuteAsync(
            "UPDATE Recommendations SET UserId = @UserId, GameId = @GameId, Reason = @Reason, CreatedAt = @CreatedAt WHERE RecommendationId = @RecommendationId;",
            recommendation
        );
    }

    /// <summary>
    ///     Elimina una recomendación de la base de datos.
    /// </summary>
    /// <param name="id">ID de la recomendación a eliminar.</param>
    public async Task Delete(int id)
    {
        using var connection = _database.CreateConnection();
        // Eliminar la recomendación con el ID especificado
        await connection.ExecuteAsync("DELETE FROM Recommendations WHERE RecommendationId = @Id;", new { Id = id });
    }
}