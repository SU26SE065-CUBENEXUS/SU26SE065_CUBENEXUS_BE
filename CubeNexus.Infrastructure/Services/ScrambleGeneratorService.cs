using System.Security.Cryptography;
using CubeNexus.Application.Interfaces.Services;

namespace CubeNexus.Infrastructure.Services;

public class ScrambleGeneratorService : IScrambleGeneratorService
{
    public string GenerateScramble(string puzzleCode, int? scrambleLength = null)
    {
        // This product's scan/verification flow intentionally uses a short scramble:
        // exactly two moves, regardless of the puzzle master-data scramble length.
        // The parameter remains for interface compatibility with existing callers.
        _ = scrambleLength;
        const int length = 2;

        var dimension = int.TryParse(puzzleCode, out var numericCode) ? numericCode / 100 : 3;
        var moves = new List<string> { "R", "L", "U", "D", "F", "B" };
        if (dimension >= 4) moves.AddRange(["Rw", "Lw", "Uw", "Dw", "Fw", "Bw"]);
        if (dimension >= 6) moves.AddRange(["3Rw", "3Lw", "3Uw", "3Dw", "3Fw", "3Bw"]);
        string[] modifiers = ["", "'", "2"];
        var parts = new List<string>(length);
        string? lastFace = null;
        for (var i = 0; i < length; i++)
        {
            string face;
            do face = moves[RandomNumberGenerator.GetInt32(moves.Count)];
            while (face.First(char.IsLetter).ToString() == lastFace);
            lastFace = face.First(char.IsLetter).ToString();
            parts.Add(face + modifiers[RandomNumberGenerator.GetInt32(modifiers.Length)]);
        }
        return string.Join(' ', parts);
    }
}
