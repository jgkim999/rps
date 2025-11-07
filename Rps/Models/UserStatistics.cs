namespace Rps.Models;

public class UserStatistics
{
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int RockCount { get; set; }
    public int PaperCount { get; set; }
    public int ScissorsCount { get; set; }
    
    public int TotalGames => Wins + Losses + Draws;
    public double WinRate => TotalGames > 0 ? (double)Wins / TotalGames * 100 : 0;
}
