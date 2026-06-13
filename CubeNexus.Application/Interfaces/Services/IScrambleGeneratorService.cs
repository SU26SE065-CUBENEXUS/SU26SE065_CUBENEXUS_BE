namespace CubeNexus.Application.Interfaces.Services;

public interface IScrambleGeneratorService
{
    string GenerateScramble(string puzzleCode, int? scrambleLength = null);
}
