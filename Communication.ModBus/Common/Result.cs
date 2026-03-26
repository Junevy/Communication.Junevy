namespace Communication.ModBus.Common
{
    public class Result<T>
    {
        public bool IsSuccess { get; set; }

        public T? Data { get; set; }

        public string? ErrorMessage { get; set; }

        public ushort? Address { get; set; }

        public static Result<T> Success(T data) => new() { IsSuccess = true, Data = data };
        public static Result<T> Fail(string errMsg, T data = default)
        {
            return new() { IsSuccess = false, ErrorMessage = errMsg, Data = data };
        }
    }
}
