namespace CubeNexus.Application.Exceptions;

public class CustomException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }
    public object? ExtraData { get; set; }

    public CustomException(string errorCode, string message, int statusCode = 400, object? extraData = null) : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        ExtraData = extraData;
    }
}
