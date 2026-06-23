namespace CubeNexus.Application.UseCases.OnlineArena;

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}
