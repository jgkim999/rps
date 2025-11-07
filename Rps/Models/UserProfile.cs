namespace Rps.Models;

public class UserProfile
{
    public long UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public int SelectedSkin { get; set; } = 0;
    public UserStatistics Statistics { get; set; } = new();
}
