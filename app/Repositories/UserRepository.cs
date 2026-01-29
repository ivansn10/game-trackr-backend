using app.Data;
using Dapper;
using static app.Controllers.IGDBController;

namespace app.repositories;

public class UserRepository
{
    private readonly Database _database;

    public UserRepository(Database database)
    {
        _database = database;
    }

    public async Task<bool> UsernameExists(string username)
    {
        using var connection = _database.CreateConnection();
        var query = "SELECT COUNT(1) FROM Users WHERE Username = @Username";
        var count = await connection.ExecuteScalarAsync<int>(query, new { Username = username });
        return count > 0;
    }

    public async Task<IEnumerable<User>> GetAll()
    {
        using var connection = _database.CreateConnection();
        return await connection.QueryAsync<User>("SELECT * FROM Users;");
    }

    public async Task<User?> GetById(int id)
    {
        using var connection = _database.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE UserId = @Id;", new { Id = id }
        );
    }

    public async Task Create(User user)
    {
        using var connection = _database.CreateConnection();

        if (await UsernameExists(user.Username))
            throw new Exception("El nombre de usuario ya está en uso. Intente con otro.");

        await connection.ExecuteAsync(
            "INSERT INTO Users (Username, Password, CreatedAt, DisplayName, AvatarUrl) VALUES (@Username, @Password, @CreatedAt, @DisplayName, @AvatarUrl);",
            user
        );
    }

    public async Task Update(User user)
    {
        using var connection = _database.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE Users SET Username = @Username, Password = @Password, Role = @Role WHERE UserId = @UserId;",
            user
        );
    }

    public async Task Delete(int id)
    {
        using var connection = _database.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM Users WHERE UserId = @Id;", new { Id = id });
    }

public async Task<UserProfile?> GetUserProfile(int userId)
{
    using var connection = _database.CreateConnection();

    var user = await connection.QueryFirstOrDefaultAsync<User>(
        "SELECT * FROM Users WHERE UserId = @UserId;",
        new { UserId = userId }
    );

    if (user == null) return null;

    var query = @"
        SELECT
            g.GameId, g.IgdbId, g.GameTitle,
            g.Description, g.ImageUrl, g.ReleaseDate,
            g.Genres, g.Platforms,
            gs.Status,
            r.Score
        FROM Games g
        INNER JOIN GameStatuses gs ON gs.GameId = g.GameId AND gs.UserId = @UserId
        LEFT JOIN Ratings r ON r.GameId = g.GameId AND r.UserId = @UserId;
    ";

    var rawGames = await connection.QueryAsync(query, new { UserId = userId });

    var games = new List<GameDto>();

    foreach (var g in rawGames)
    {
        games.Add(new GameDto
        {
            GameId = g.gameid,
            IgdbId = g.igdbid,
            GameTitle = g.gametitle,
            Description = g.description ?? "",
            ImageUrl = g.imageurl ?? "",
            ReleaseDate = g.releasedate ?? "",
            Genres = (g.genres as string[])?.ToList() ?? new List<string>(),
            Platforms = (g.platforms as string[])?.ToList() ?? new List<string>(),
            Status = g.status ?? "None",
            Score = g.score != null ? (double?)g.score : null
        });
    }

    return new UserProfile
    {
        DisplayName = user.DisplayName ?? "",
        AvatarUrl = user.AvatarUrl ?? "",
        CreatedAt = user.CreatedAt.ToString("o"),
        GameCollection = games
    };
}


public async Task SaveUserProfile(int userId, UserProfile profile)
{
    using var connection = _database.CreateConnection();
    connection.Open();
    using var transaction = connection.BeginTransaction();

    // 🔹 Actualizar nombre + avatar
    await connection.ExecuteAsync(
        @"UPDATE Users 
          SET DisplayName = @DisplayName, AvatarUrl = @AvatarUrl 
          WHERE UserId = @UserId;",
        new
        {
            profile.DisplayName,
            profile.AvatarUrl,
            UserId = userId
        }, transaction
    );

    // 🔹 Eliminar datos anteriores
    await connection.ExecuteAsync("DELETE FROM GameStatuses WHERE UserId = @UserId;", new { UserId = userId }, transaction);
    await connection.ExecuteAsync("DELETE FROM Ratings WHERE UserId = @UserId;", new { UserId = userId }, transaction);
    await connection.ExecuteAsync("DELETE FROM Favorites WHERE UserId = @UserId;", new { UserId = userId }, transaction);

    // 🔹 Guardar juegos
    foreach (var game in profile.GameCollection)
    {
        // Insertar/Actualizar juego por IgdbId (NO GameId)
        await connection.ExecuteAsync(@"
            INSERT INTO Games (IgdbId, GameTitle, Description, ImageUrl, ReleaseDate, Genres, Platforms)
            VALUES (@IgdbId, @GameTitle, @Description, @ImageUrl, @ReleaseDate, @Genres, @Platforms)
            ON CONFLICT (IgdbId) DO UPDATE SET 
                GameTitle = EXCLUDED.GameTitle,
                Description = EXCLUDED.Description,
                ImageUrl = EXCLUDED.ImageUrl,
                ReleaseDate = EXCLUDED.ReleaseDate,
                Genres = EXCLUDED.Genres,
                Platforms = EXCLUDED.Platforms;",
            new
            {
                game.IgdbId,
                game.GameTitle,
                game.Description,
                game.ImageUrl,
                game.ReleaseDate,
                Genres = game.Genres?.ToArray() ?? Array.Empty<string>(),
                Platforms = game.Platforms?.ToArray() ?? Array.Empty<string>()
            }, transaction
        );

        // 🔹 Obtener GameId real desde la base de datos
        var dbGameId = await connection.QuerySingleAsync<int>(
            "SELECT GameId FROM Games WHERE IgdbId = @IgdbId;",
            new { game.IgdbId }, transaction
        );

        // 🔹 Insertar estado
        await connection.ExecuteAsync(@"
            INSERT INTO GameStatuses (UserId, GameId, Status)
            VALUES (@UserId, @GameId, @Status);",
            new
            {
                UserId = userId,
                GameId = dbGameId,
                Status = game.Status ?? "None"
            }, transaction
        );

        // 🔹 Insertar puntuación si aplica
        if (game.Score.HasValue)
        {
            await connection.ExecuteAsync(@"
                INSERT INTO Ratings (UserId, GameId, Score)
                VALUES (@UserId, @GameId, @Score);",
                new
                {
                    UserId = userId,
                    GameId = dbGameId,
                    Score = (int)Math.Round(game.Score.Value)
                }, transaction
            );
        }

        // 🔹 Insertar como favorito si aplica
        if (game.Status is "Wishlist" or "Owned" or "Playing")
        {
            await connection.ExecuteAsync(@"
                INSERT INTO Favorites (UserId, GameId)
                VALUES (@UserId, @GameId);",
                new
                {
                    UserId = userId,
                    GameId = dbGameId
                }, transaction
            );
        }
    }

    transaction.Commit();
}


}