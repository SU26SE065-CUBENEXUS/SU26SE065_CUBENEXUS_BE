using CubeNexus.Domain.Entities;

namespace CubeNexus.Domain.Services;

public interface IEloCalculator
{
    (int player1NewElo, int player2NewElo, decimal p1Expected, decimal p2Expected) Calculate(
        int player1Elo, int player1K, decimal player1Score,
        int player2Elo, int player2K, decimal player2Score);
}

public class EloCalculator : IEloCalculator
{
    public (int player1NewElo, int player2NewElo, decimal p1Expected, decimal p2Expected) Calculate(
        int player1Elo, int player1K, decimal player1Score,
        int player2Elo, int player2K, decimal player2Score)
    {
        // Expected Score = 1 / (1 + 10 ^ ((OpponentElo - PlayerElo) / 400))
        decimal p1Expected = 1.0m / (1.0m + (decimal)Math.Pow(10, (player2Elo - player1Elo) / 400.0));
        decimal p2Expected = 1.0m / (1.0m + (decimal)Math.Pow(10, (player1Elo - player2Elo) / 400.0));

        // NewElo = OldElo + K * (ActualScore - ExpectedScore)
        int p1NewElo = player1Elo + (int)Math.Round(player1K * (player1Score - p1Expected));
        int p2NewElo = player2Elo + (int)Math.Round(player2K * (player2Score - p2Expected));

        return (p1NewElo, p2NewElo, p1Expected, p2Expected);
    }
}
