public sealed class VmmException : Exception
{
    public VmmException()
    {
    }

    public VmmException(string message)
        : base(message)
    {
    }

    public VmmException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
