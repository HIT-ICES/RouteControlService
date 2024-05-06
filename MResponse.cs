namespace RouteControlService;

public record EmptyObject
{
    public static EmptyObject Default { get; } = new();
}

public record MResponse<T>(string Message, int Code, T Data);

public record MResponse(string Message, int Code) : MResponse<EmptyObject>(Message, Code, EmptyObject.Default)
{
    public const int SuccessCode = 0;
    public const int FailedCode = 1;

    public static MResponse Successful() { return new MResponse("success", SuccessCode); }

    public static MResponse<T> Successful<T>(T value)
    where T : notnull
    {
        return new MResponse<T>("success", SuccessCode, value);
    }

    public static MResponse Failed(string message) { return new MResponse(message, FailedCode); }
}