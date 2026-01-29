using app.repositories;

namespace app.Services;

// Servicio para autenticación de usuarios
public class AuthService
{
    // Repositorio para acceder a los datos de usuarios
    private readonly UserRepository _userRepository;

    // Constructor que recibe el repositorio mediante inyección de dependencias
    public AuthService(UserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    // Validación usando hash de contraseña
     public async Task<User?> ValidateUser(string username, string password)
    {
        var users = await _userRepository.GetAll();

        // Buscar por nombre de usuario
        var user = users.FirstOrDefault(u => u.Username == username);
        if (user == null) return null;

        // Verificar contraseña hasheada
        return BCrypt.Net.BCrypt.Verify(password, user.Password) ? user : null;
    }

    // Método para crear hash de contraseña
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }
}