using LiteBus.Commands.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Rps.Hubs;

namespace Rps.Handlers.Commands;

public record SendMessageCommand(string User, string Message) : ICommand;

public class SendMessageCommandHandler : ICommandHandler<SendMessageCommand>
{
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<SendMessageCommandHandler> _logger;

    public SendMessageCommandHandler(IHubContext<GameHub> hubContext, ILogger<SendMessageCommandHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task HandleAsync(SendMessageCommand command, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing SendMessage command from user: {User}", command.User);

        // Validate user
        if (string.IsNullOrWhiteSpace(command.User))
        {
            _logger.LogWarning("SendMessage called with empty user");
            throw new ArgumentException("사용자 이름이 비어있습니다");
        }

        // Validate message
        if (string.IsNullOrWhiteSpace(command.Message))
        {
            _logger.LogWarning("SendMessage called with empty message from user: {User}", command.User);
            throw new ArgumentException("메시지가 비어있습니다");
        }

        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", command.User, command.Message, cancellationToken);

            _logger.LogDebug("Message sent from user: {User}", command.User);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message from user: {User}", command.User);
            throw new InvalidOperationException("메시지 전송에 실패했습니다", ex);
        }
    }
}
