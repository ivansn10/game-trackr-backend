-- Crear la tabla de Usuarios
CREATE TABLE Users (
    UserId SERIAL PRIMARY KEY,
    Username VARCHAR(100) NOT NULL UNIQUE,
    Password VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    DisplayName VARCHAR(100),
    AvatarUrl TEXT
);

-- Crear la tabla de Juegos
CREATE TABLE Games (
    GameId SERIAL PRIMARY KEY,
    IgdbId INT NOT NULL UNIQUE,
    GameTitle VARCHAR(255) NOT NULL,
    Description TEXT,
    ImageUrl TEXT,
    ReleaseDate VARCHAR(50),
    Genres TEXT[],
    Platforms TEXT[]
);

-- Crear la tabla de Calificaciones
CREATE TABLE Ratings (
    RatingId SERIAL PRIMARY KEY,
    UserId INT NOT NULL,
    GameId INT NOT NULL,
    Score INT NOT NULL CHECK (Score >= 1 AND Score <= 10),
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
    FOREIGN KEY (GameId) REFERENCES Games(GameId) ON DELETE CASCADE
);

-- Crear la tabla de Estados del Juego
CREATE TABLE GameStatuses (
    StatusId SERIAL PRIMARY KEY,
    UserId INT NOT NULL,
    GameId INT NOT NULL,
    Status VARCHAR(10) NOT NULL CHECK (Status IN ('None', 'Wishlist', 'Owned', 'Playing', 'Completed', 'Abandoned')),
    UpdatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
    FOREIGN KEY (GameId) REFERENCES Games(GameId) ON DELETE CASCADE,
    UNIQUE(UserId, GameId)
);

-- Crear la tabla de Favoritos
CREATE TABLE Favorites (
    FavoriteId SERIAL PRIMARY KEY,
    UserId INT NOT NULL,
    GameId INT NOT NULL,
    AddedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
    FOREIGN KEY (GameId) REFERENCES Games(GameId) ON DELETE CASCADE,
    UNIQUE(UserId, GameId)
);

-- Crear la tabla de Recomendaciones
CREATE TABLE Recommendations (
    RecommendationId SERIAL PRIMARY KEY,
    UserId INT NOT NULL,
    GameId INT NOT NULL,
    IgdbId INT NOT NULL,
    GameTitle VARCHAR(255) NOT NULL,
    Reason TEXT NOT NULL,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
    FOREIGN KEY (GameId) REFERENCES Games(GameId) ON DELETE CASCADE,
    UNIQUE(UserId, GameId)
);

-- Índices para mejorar la eficiencia
CREATE INDEX idx_users_username ON Users(Username);
CREATE INDEX idx_games_igdb ON Games(IgdbId);
CREATE INDEX idx_ratings_user ON Ratings(UserId);
CREATE INDEX idx_ratings_game ON Ratings(GameId);
CREATE INDEX idx_favorites_user ON Favorites(UserId);
CREATE INDEX idx_favorites_game ON Favorites(GameId);
CREATE INDEX idx_recommendations_user ON Recommendations(UserId);
CREATE INDEX idx_recommendations_game ON Recommendations(GameId);
CREATE INDEX idx_gamestatus_user_game ON GameStatuses(UserId, GameId); 