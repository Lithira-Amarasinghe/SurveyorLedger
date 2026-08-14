namespace SurveyorLedger.Core.Exceptions;

public class AppException : Exception
{
    public string Code { get; set; }
    public int StatusCode { get; set; }

    public AppException(string code, string message, int statusCode = 400)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}

public class UnauthorizedException : AppException
{
    public UnauthorizedException(string message = "Unauthorized")
        : base(Constants.ErrorCodes.Unauthorized, message, 401) { }
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string message = "Forbidden")
        : base(Constants.ErrorCodes.Forbidden, message, 403) { }
}

public class NotFoundException : AppException
{
    public NotFoundException(string message)
        : base(Constants.ErrorCodes.UserNotFound, message, 404) { }
}

public class ValidationException : AppException
{
    public ValidationException(string message)
        : base(Constants.ErrorCodes.ValidationFailed, message, 400) { }
}

public class ConflictException : AppException
{
    public ConflictException(string message)
        : base(Constants.ErrorCodes.ValidationFailed, message, 409) { }
}
