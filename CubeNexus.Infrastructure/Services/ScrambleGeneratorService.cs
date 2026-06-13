using CubeNexus.Application.Interfaces.Services;

namespace CubeNexus.Infrastructure.Services;

public class ScrambleGeneratorService : IScrambleGeneratorService
{
    public string GenerateScramble(string puzzleCode, int? scrambleLength = null)
    {
        int length = scrambleLength ?? 20;
        string[] moves = ["R", "L", "U", "D", "F", "B"];
        string[] modifiers = ["", "'", "2"];
        var rand = new Random();
        var sequenceParts = new List<string>();
        string lastFace = "";

        for (int i = 0; i < length; i++)
        {
            string face;
            do
            {
                face = moves[rand.Next(moves.Length)];
            } while (face == lastFace);

            lastFace = face;
            string mod = modifiers[rand.Next(modifiers.Length)];
            sequenceParts.Add(face + mod);
        }

        return string.Join(" ", sequenceParts);
    }
}
