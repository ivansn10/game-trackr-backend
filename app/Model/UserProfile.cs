using static app.Controllers.IGDBController;

public class UserProfile
    {
        public string DisplayName { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public List<GameDto> GameCollection { get; set; } = new();
    }