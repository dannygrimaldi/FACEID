namespace Face.Api.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public class FaceNotDetectedException : DomainException
{
    public FaceNotDetectedException() : base("No face detected in the image") { }
}

public class InvalidImageException : DomainException
{
    public InvalidImageException(string message) : base(message) { }
}