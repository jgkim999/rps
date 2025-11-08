using LiteBus.Commands.Abstractions;
using Rps.Models;
using Rps.Services;

namespace Rps.Handlers.Commands;

public record LoginUserCommand(string Nickname, string ConnectionId) : ICommand<UserProfile>;

public class LoginUserCommandHandler : ICommandHandler<LoginUserCommand, UserProfile>
{
    private readonly IUserService _userService;
    private readonly ILogger<LoginUserCommandHandler> _logger;

    public LoginUserCommandHandler(IUserService userService, ILogger<LoginUserCommandHandler> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    public async Task<UserProfile> HandleAsync(LoginUserCommand command, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing LoginUser command for nickname: {Nickname}, ConnectionId: {ConnectionId}",
            command.Nickname, command.ConnectionId);

        // Validate nickname
        if (string.IsNullOrWhiteSpace(command.Nickname))
        {
            _logger.LogWarning("LoginUser called with empty nickname from ConnectionId: {ConnectionId}", command.ConnectionId);
            throw new ArgumentException("닉네임을 입력해주세요");
        }

        if (command.Nickname.Length < 2)
        {
            _logger.LogWarning("LoginUser called with too short nickname: {Nickname} from ConnectionId: {ConnectionId}",
                command.Nickname, command.ConnectionId);
            throw new ArgumentException("닉네임은 최소 2자 이상이어야 합니다");
        }

        if (command.Nickname.Length > 20)
        {
            _logger.LogWarning("LoginUser called with too long nickname from ConnectionId: {ConnectionId}", command.ConnectionId);
            throw new ArgumentException("닉네임은 최대 20자까지 가능합니다");
        }

        try
        {
            var userProfile = await _userService.LoginOrCreateUserAsync(command.Nickname, command.ConnectionId);

            _logger.LogInformation("User logged in successfully. UserId:{UserId}, Nickname:{Nickname}, SelectedSkin:{SelectedSkin}, ConnectionId:{ConnectionId}",
                userProfile.UserId, userProfile.Nickname, userProfile.SelectedSkin, command.ConnectionId);

            return userProfile;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument in LoginUser for nickname: {Nickname}", command.Nickname);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Operation failed in LoginUser for nickname: {Nickname}", command.Nickname);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in LoginUser for nickname: {Nickname}", command.Nickname);
            throw new InvalidOperationException("로그인 처리 중 오류가 발생했습니다. 다시 시도해주세요", ex);
        }
    }
}
