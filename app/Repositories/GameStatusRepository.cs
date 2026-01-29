using app.Data;
using app.Model;
using Dapper;

namespace app.repositories;

/// <summary>
///     Repositorio que maneja operaciones de acceso a datos para la entidad GameStatus.
/// </summary>
public class GameStatusRepository
{
    // Conexión a la base de datos
    private readonly Database _database;

    /// <summary>
    ///     Constructor que recibe la dependencia de acceso a datos.
    /// </summary>
    public GameStatusRepository(Database database)
    {
        _database = database;
    }

    /// <summary>
    ///     Obtiene todos los registros de estados de juegos de la base de datos.
    /// </summary>
    /// <returns>Colección de todos los estados de juegos.</returns>
    public async Task<IEnumerable<GameStatus>> GetAll()
    {
        // Creamos una conexión nueva a la base de datos
        using var connection = _database.CreateConnection();
        // Ejecutamos una consulta SQL simple para obtener todos los estados
        return await connection.QueryAsync<GameStatus>("SELECT * FROM GameStatuses;");
    }

    /// <summary>
    ///     Obtiene un estado de juego específico por su ID.
    /// </summary>
    /// <param name="id">ID del estado a buscar.</param>
    /// <returns>El estado encontrado o null si no existe.</returns>
    public async Task<GameStatus?> GetById(int id)
    {
        using var connection = _database.CreateConnection();
        // Usamos QueryFirstOrDefaultAsync para obtener un solo registro o null
        return await connection.QueryFirstOrDefaultAsync<GameStatus>(
            "SELECT * FROM GameStatuses WHERE StatusId = @Id;", new { Id = id }
        );
    }

    /// <summary>
    ///     Establece o actualiza el estado de un juego para un usuario.
    ///     Si la combinación usuario/juego ya existe, actualiza el estado.
    ///     Si no existe, crea un nuevo registro.
    /// </summary>
    /// <param name="userId">ID del usuario.</param>
    /// <param name="gameId">ID del juego.</param>
    /// <param name="status">Nuevo estado para el juego (Wishlist, Owned, Playing, etc).</param>
    public async Task SetStatus(int userId, int gameId, string status)
    {
        // Consulta UPSERT (INSERT o UPDATE si ya existe)
        var query = @"
       INSERT INTO GameStatuses (UserId, GameId, Status, UpdatedAt)
       VALUES (@UserId, @GameId, @Status, NOW())
       ON CONFLICT (UserId, GameId) DO UPDATE
       SET Status = EXCLUDED.Status, UpdatedAt = NOW();
   ";

        using var connection = _database.CreateConnection();
        // Ejecutamos la consulta con los parámetros proporcionados
        await connection.ExecuteAsync(query, new { UserId = userId, GameId = gameId, Status = status });
    }


    /// <summary>
    ///     Elimina un estado de juego de la base de datos.
    /// </summary>
    /// <param name="id">ID del estado a eliminar.</param>
    public async Task Delete(int id)
    {
        using var connection = _database.CreateConnection();
        // Eliminar el estado con el ID especificado
        await connection.ExecuteAsync("DELETE FROM GameStatuses WHERE StatusId = @Id;", new { Id = id });
    }
}