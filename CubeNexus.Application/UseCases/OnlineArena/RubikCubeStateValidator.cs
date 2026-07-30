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

internal sealed class BatchScrambleValidationResult
{
    public bool IsValid { get; init; }
    public bool IsMatchAll { get; init; }
    public string? Reason { get; init; }
    public List<string> MismatchedCenterColors { get; init; } = [];
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

    private static readonly Dictionary<string, string> ColorToFaceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["white"] = "U",
        ["yellow"] = "D",
        ["green"] = "F",
        ["blue"] = "B",
        ["red"] = "R",
        ["orange"] = "L"
    };

    public static BatchScrambleValidationResult ValidateScrambleBatch(
        string scrambleSequence,
        List<ScrambleCheckBatchFaceDto> faces)
    {
        if (faces == null || faces.Count != 5)
        {
            return new BatchScrambleValidationResult
            {
                IsValid = false,
                Reason = "Scramble check requires exactly 5 scanned faces."
            };
        }

        var validColorSet = Colors.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var centers = faces.Select(f => (f.CenterColor ?? string.Empty).Trim().ToLowerInvariant()).ToList();

        if (centers.Any(c => !validColorSet.Contains(c)))
        {
            return new BatchScrambleValidationResult
            {
                IsValid = false,
                Reason = "Batch contains an invalid center color."
            };
        }

        if (centers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 5)
        {
            return new BatchScrambleValidationResult
            {
                IsValid = false,
                Reason = "Faces in batch must have 5 distinct center colors."
            };
        }

        var expectedState = BuildExpectedCubeStateForScramble(scrambleSequence);
        var mismatchedCenters = new List<string>();

        foreach (var faceDto in faces)
        {
            var centerColor = (faceDto.CenterColor ?? string.Empty).Trim().ToLowerInvariant();

            List<string> observedStickers = [];
            if (faceDto.Grid3x3 != null && faceDto.Grid3x3.Count == 3 && faceDto.Grid3x3.All(r => r.Count == 3))
            {
                observedStickers = faceDto.Grid3x3.SelectMany(r => r).Select(c => c.Trim().ToLowerInvariant()).ToList();
            }
            else if (faceDto.Stickers != null && faceDto.Stickers.Count == 9)
            {
                observedStickers = faceDto.Stickers.Select(c => c.Trim().ToLowerInvariant()).ToList();
            }
            else
            {
                return new BatchScrambleValidationResult
                {
                    IsValid = false,
                    Reason = $"Face with center '{centerColor}' does not contain exactly 9 stickers."
                };
            }

            if (observedStickers.Any(c => !validColorSet.Contains(c)))
            {
                return new BatchScrambleValidationResult
                {
                    IsValid = false,
                    Reason = $"Face with center '{centerColor}' contains an invalid sticker color."
                };
            }

            if (!string.Equals(observedStickers[4], centerColor, StringComparison.OrdinalIgnoreCase))
            {
                return new BatchScrambleValidationResult
                {
                    IsValid = false,
                    Reason = $"Center sticker (index 4) of face '{centerColor}' must match its center color."
                };
            }

            if (!ColorToFaceMap.TryGetValue(centerColor, out var logicalFace))
            {
                return new BatchScrambleValidationResult
                {
                    IsValid = false,
                    Reason = $"Center color '{centerColor}' cannot be mapped to a logical face."
                };
            }

            var expectedStickers = expectedState[logicalFace]
                .SelectMany(row => row)
                .Select(c => c.Trim().ToLowerInvariant())
                .ToList();

            if (!MatchesAnyRotation(observedStickers, expectedStickers))
            {
                mismatchedCenters.Add(centerColor.ToUpperInvariant());
            }
        }

        return new BatchScrambleValidationResult
        {
            IsValid = true,
            IsMatchAll = mismatchedCenters.Count == 0,
            Reason = mismatchedCenters.Count == 0 ? "Scramble check passed." : "Some faces do not match scramble.",
            MismatchedCenterColors = mismatchedCenters
        };
    }

    private static bool MatchesAnyRotation(List<string> s, List<string> e)
    {
        if (SequenceEquals(s, e)) return true;

        // 90 deg clockwise: [s6, s3, s0, s7, s4, s1, s8, s5, s2]
        var r90 = new List<string> { s[6], s[3], s[0], s[7], s[4], s[1], s[8], s[5], s[2] };
        if (SequenceEquals(r90, e)) return true;

        // 180 deg: [s8, s7, s6, s5, s4, s3, s2, s1, s0]
        var r180 = new List<string> { s[8], s[7], s[6], s[5], s[4], s[3], s[2], s[1], s[0] };
        if (SequenceEquals(r180, e)) return true;

        // 270 deg clockwise: [s2, s5, s8, s1, s4, s7, s0, s3, s6]
        var r270 = new List<string> { s[2], s[5], s[8], s[1], s[4], s[7], s[0], s[3], s[6] };
        if (SequenceEquals(r270, e)) return true;

        return false;
    }

    private static bool SequenceEquals(List<string> a, List<string> b)
    {
        for (var i = 0; i < 9; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

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
