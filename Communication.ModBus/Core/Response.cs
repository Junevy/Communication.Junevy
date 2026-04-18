namespace Communication.Modbus.Core
{
    /// <summary>
    /// ModBus 响应数据类，用于封装 ModBus 响应数据。
    /// </summary>
    public class Response
    {
        /// <summary>
        /// 是否成功响应。
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 响应数据。
        /// </summary>
        public byte[]? Data { get; set; }

        public byte[]? RawData { get; set; }
        
        /// <summary>
        /// 错误信息。
        /// </summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// 响应地址。
        /// </summary>
        public ushort? Address { get; set; }

        /// <summary>
        /// 成功响应。
        /// </summary>
        /// <param name="data">响应数据。</param>
        /// <returns>成功响应对象。</returns>
        public static Response Success(byte[] data) => new() { IsSuccess = true, Data = data };
        
        /// <summary>
        /// 失败响应。
        /// </summary>
        /// <param name="errMsg">错误信息。</param>
        /// <param name="data">响应数据。</param>
        /// <returns>失败响应对象。</returns>
        public static Response Fail(string errMsg, byte[]? data = default)
        {
            return new() { IsSuccess = false, ErrorMessage = errMsg, Data = data };
        }
    }
}
