namespace SurveyorLedger.API.Models.Responses;

/// <summary>
/// Generic API response wrapper for all endpoints.
/// </summary>
/// <typeparam name="T">The type of the data payload.</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Indicates whether the request was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The response data payload. Null if request failed or no data is returned.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Error message. Null on success.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Validation errors keyed by field name. Null if no validation errors.
    /// </summary>
    public Dictionary<string, string[]>? Errors { get; set; }

    /// <summary>
    /// Creates a successful response with data.
    /// </summary>
    public static ApiResponse<T> Ok(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    /// <summary>
    /// Creates a failure response with an error message.
    /// </summary>
    public static ApiResponse<T> Fail(string message, Dictionary<string, string[]>? errors = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Errors = errors
        };
    }
}
