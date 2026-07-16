namespace GamingMode.Models;

public sealed class ApiResult
{
	public bool Ok { get; set; } = true;

	public string Message { get; set; } = "";

	public ModeStatus? Status { get; set; }

	public static ApiResult Success(string message, ModeStatus? status = null)
	{
		return new ApiResult
		{
			Ok = true,
			Message = message,
			Status = status
		};
	}

	public static ApiResult Failure(string message, ModeStatus? status = null)
	{
		return new ApiResult
		{
			Ok = false,
			Message = message,
			Status = status
		};
	}
}
