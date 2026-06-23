using CubeNexus.Application.DTOs.OnlineArena;

namespace CubeNexus.Application.UseCases.OnlineArena;

internal sealed class CubeStateBasicValidation
{
    public bool IsValid { get; init; }
    public string? Reason { get; init; }
    public List<string> Missing { get; init; } = [];
    public Dictionary<string, int> ColorCounts { get; init; } = [];
}

internal sealed class CubeStateComparisonResult
{
    public bool Matched { get; init; }
    public int MatchedStickerCount { get; init; }
    public int MismatchedStickerCount { get; init; }
    public List<CubeScanStickerMismatchDto> Mismatches { get; init; } = [];
}

internal static class RubikCubeStateValidator
{
    private static readonly string[] Faces = ["U", "R", "F", "D", "L", "B"];
    private static readonly string[] Colors = ["white", "red", "green", "yellow", "orange", "blue"];
    private static readonly Dictionary<string, string> SolvedFaceColors = new()
    {
        ["U"] = "white",
        ["R"] = "red",
        ["F"] = "green",
        ["D"] = "yellow",
        ["L"] = "orange",
        ["B"] = "blue"
    };

    public static CubeStateBasicValidation ValidateBasicCubeState(Dictionary<string, List<List<string>>> cubeState)
    {
        var missing = Faces.Where(face => !cubeState.ContainsKey(face)).ToList();
        var counts = Colors.ToDictionary(color => color, _ => 0);
        if (missing.Count > 0)
            return new CubeStateBasicValidation { IsValid = false, Reason = "Missing cube faces.", Missing = missing, ColorCounts = counts };

        foreach (var face in Faces)
        {
            var grid = cubeState[face];
            if (grid.Count != 3 || grid.Any(row => row.Count != 3))
                return new CubeStateBasicValidation { IsValid = false, Reason = $"Face {face} must be a 3x3 grid.", ColorCounts = counts };

            foreach (var color in grid.SelectMany(row => row).Select(color => color.Trim().ToLowerInvariant()))
            {
                if (!counts.ContainsKey(color))
                    return new CubeStateBasicValidation { IsValid = false, Reason = $"Unsupported color '{color}'.", ColorCounts = counts };
                counts[color]++;
            }
        }

        var invalidColor = counts.FirstOrDefault(item => item.Value != 9);
        if (!string.IsNullOrWhiteSpace(invalidColor.Key))
            return new CubeStateBasicValidation { IsValid = false, Reason = "Each Rubik color must appear exactly 9 times.", ColorCounts = counts };

        return new CubeStateBasicValidation { IsValid = true, ColorCounts = counts };
    }

    public static bool IsSolved(Dictionary<string, List<List<string>>> cubeState)
        => Faces.All(face =>
        {
            var expected = cubeState[face][1][1].Trim().ToLowerInvariant();
            return cubeState[face].SelectMany(row => row).All(color => color.Trim().ToLowerInvariant() == expected);
        });

    public static bool MatchesScramble(Dictionary<string, List<List<string>>> cubeState, string scrambleSequence)
    {
        return CompareCubeStates(BuildExpectedCubeStateForScramble(scrambleSequence), cubeState).Matched;
    }

    public static Dictionary<string, List<List<string>>> BuildExpectedCubeStateForScramble(string scrambleSequence)
    {
        var expected = RubikCubeSimulator.CreateSolved();
        expected.ApplyScramble(scrambleSequence);
        return expected.ToCubeState();
    }

    public static Dictionary<string, List<List<string>>> BuildSolvedCubeState()
        => RubikCubeSimulator.CreateSolved().ToCubeState();

    public static CubeStateComparisonResult CompareCubeStates(
        Dictionary<string, List<List<string>>> expectedState,
        Dictionary<string, List<List<string>>> observedState)
    {
        var mismatches = new List<CubeScanStickerMismatchDto>();
        var matchedStickerCount = 0;

        foreach (var face in Faces)
        {
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    var expected = expectedState[face][row][column].Trim().ToLowerInvariant();
                    var observed = observedState[face][row][column].Trim().ToLowerInvariant();
                    if (string.Equals(expected, observed, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedStickerCount++;
                        continue;
                    }

                    mismatches.Add(new CubeScanStickerMismatchDto
                    {
                        Face = face,
                        Row = row,
                        Column = column,
                        Expected = expected,
                        Observed = observed
                    });
                }
            }
        }

        return new CubeStateComparisonResult
        {
            Matched = mismatches.Count == 0,
            MatchedStickerCount = matchedStickerCount,
            MismatchedStickerCount = mismatches.Count,
            Mismatches = mismatches
        };
    }

    private static bool GridEquals(List<List<string>> expected, List<List<string>> actual)
    {
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                if (!string.Equals(expected[row][col], actual[row][col].Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        return true;
    }

    private sealed record Sticker(int X, int Y, int Z, int Nx, int Ny, int Nz, string Color);

    private sealed class RubikCubeSimulator
    {
        private List<Sticker> _stickers = [];

        public static RubikCubeSimulator CreateSolved()
        {
            var cube = new RubikCubeSimulator();
            foreach (var face in Faces)
            {
                for (var index = 0; index < 9; index++)
                {
                    var (x, y, z, nx, ny, nz) = FaceIndexToStickerPose(face, index);
                    cube._stickers.Add(new Sticker(x, y, z, nx, ny, nz, SolvedFaceColors[face]));
                }
            }
            return cube;
        }

        public void ApplyScramble(string scrambleSequence)
        {
            foreach (var token in scrambleSequence.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                ApplyMove(token);
            }
        }

        public Dictionary<string, List<List<string>>> ToCubeState()
        {
            var result = Faces.ToDictionary(face => face, _ => new List<List<string>>
            {
                new() { string.Empty, string.Empty, string.Empty },
                new() { string.Empty, string.Empty, string.Empty },
                new() { string.Empty, string.Empty, string.Empty }
            });

            foreach (var sticker in _stickers)
            {
                var (face, index) = StickerPoseToFaceIndex(sticker);
                result[face][index / 3][index % 3] = sticker.Color;
            }

            return result;
        }

        private void ApplyMove(string token)
        {
            var face = token[0];
            var turns = token.EndsWith("2", StringComparison.Ordinal) ? 2 : token.EndsWith("'", StringComparison.Ordinal) ? 3 : 1;
            for (var i = 0; i < turns; i++)
                ApplyClockwiseQuarter(face);
        }

        private void ApplyClockwiseQuarter(char face)
        {
            _stickers = _stickers.Select(sticker => IsOnLayer(sticker, face) ? RotateStickerClockwise(sticker, face) : sticker).ToList();
        }

        private static bool IsOnLayer(Sticker sticker, char face)
            => face switch
            {
                'U' => sticker.Y == 1,
                'D' => sticker.Y == -1,
                'R' => sticker.X == 1,
                'L' => sticker.X == -1,
                'F' => sticker.Z == 1,
                'B' => sticker.Z == -1,
                _ => false
            };

        private static Sticker RotateStickerClockwise(Sticker sticker, char face)
        {
            var (x, y, z) = RotateVector(sticker.X, sticker.Y, sticker.Z, face);
            var (nx, ny, nz) = RotateVector(sticker.Nx, sticker.Ny, sticker.Nz, face);
            return sticker with { X = x, Y = y, Z = z, Nx = nx, Ny = ny, Nz = nz };
        }

        private static (int X, int Y, int Z) RotateVector(int x, int y, int z, char face)
            => face switch
            {
                'U' => (-z, y, x),
                'D' => (z, y, -x),
                'R' => (x, z, -y),
                'L' => (x, -z, y),
                'F' => (y, -x, z),
                'B' => (-y, x, z),
                _ => (x, y, z)
            };
    }

    private static (int X, int Y, int Z, int Nx, int Ny, int Nz) FaceIndexToStickerPose(string face, int index)
    {
        var row = index / 3;
        var col = index % 3;
        return face switch
        {
            "U" => (col - 1, 1, row - 1, 0, 1, 0),
            "D" => (col - 1, -1, 1 - row, 0, -1, 0),
            "F" => (col - 1, 1 - row, 1, 0, 0, 1),
            "B" => (1 - col, 1 - row, -1, 0, 0, -1),
            "R" => (1, 1 - row, 1 - col, 1, 0, 0),
            "L" => (-1, 1 - row, col - 1, -1, 0, 0),
            _ => throw new InvalidOperationException($"Unsupported face {face}.")
        };
    }

    private static (string Face, int Index) StickerPoseToFaceIndex(Sticker sticker)
    {
        if (sticker.Ny == 1) return ("U", (sticker.Z + 1) * 3 + sticker.X + 1);
        if (sticker.Ny == -1) return ("D", (1 - sticker.Z) * 3 + sticker.X + 1);
        if (sticker.Nz == 1) return ("F", (1 - sticker.Y) * 3 + sticker.X + 1);
        if (sticker.Nz == -1) return ("B", (1 - sticker.Y) * 3 + 1 - sticker.X);
        if (sticker.Nx == 1) return ("R", (1 - sticker.Y) * 3 + 1 - sticker.Z);
        if (sticker.Nx == -1) return ("L", (1 - sticker.Y) * 3 + sticker.Z + 1);
        throw new InvalidOperationException("Invalid sticker normal.");
    }
}
