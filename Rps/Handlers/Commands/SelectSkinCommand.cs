using LiteBus.Commands.Abstractions;
using Rps.Services;

namespace Rps.Handlers.Commands;

public record SelectSkinCommand(long UserId, int SkinId) : ICommand;

public class SelectSkinCommandHandler : ICommandHandler<SelectSkinCommand>
{
    private readonly IUserService _userService;
    private readonly ILogger<SelectSkinCommandHandler> _logger;

    public SelectSkinCommandHandler(IUserService userService, ILogger<SelectSkinCommandHandler> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    public async Task HandleAsync(SelectSkinCommand command, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing SelectSkin command for UserId: {UserId}, SkinId: {SkinId}",
            command.UserId, command.SkinId);

        // Validate userId
        if (command.UserId <= 0)
        {
            _logger.LogWarning("SelectSkin called with invalid userId: {UserId}", command.UserId);
            throw new ArgumentException("잘못된 사용자 ID입니다");
        }

        // Validate skinId
        if (command.SkinId < 0)
        {
            _logger.LogWarning("SelectSkin called with invalid skinId: {SkinId} for userId: {UserId}",
                command.SkinId, command.UserId);
            throw new ArgumentException("잘못된 스킨 ID입니다");
        }

        try
        {
            await _userService.UpdateUserSkinAsync(command.UserId, command.SkinId);

            _logger.LogInformation("Skin selected successfully. UserId:{UserId}, SkinId:{SkinId}",
                command.UserId, command.SkinId);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument in SelectSkin for userId: {UserId}, skinId: {SkinId}",
                command.UserId, command.SkinId);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Operation failed in SelectSkin for userId: {UserId}, skinId: {SkinId}",
                command.UserId, command.SkinId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SelectSkin for userId: {UserId}, skinId: {SkinId}",
                command.UserId, command.SkinId);
            throw new InvalidOperationException("스킨 선택 중 오류가 발생했습니다. 다시 시도해주세요", ex);
        }
    }
}
