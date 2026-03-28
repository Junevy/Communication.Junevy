namespace Communication.ModBus.Common
{
    /// <summary>
    /// 用于注入ISerilog，推荐主程序使用 Adapter 模式注入ISerilog对象。
    /// </summary>
    public interface ISerilog
    {
        void Verbose(string msg);
        void Verbose<T>(string msgTemplate, T propertyValue);
        void Verbose<T0, T1>(string msgTemplate, T0 propertyValue0, T1 propertyValue1);
        void Verbose<T0, T1, T2>(string msgTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2);

        void Debug(string msg);
        void Debug<T>(string msgTemplate, T propertyValue);
        void Debug<T0, T1>(string msgTemplate, T0 propertyValue0, T1 propertyValue1);
        void Debug<T0, T1, T2>(string msgTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2);

        void Information(string msg);
        void Information<T>(string msgTemplate, T propertyValue);
        void Information<T0, T1>(string msgTemplate, T0 propertyValue0, T1 propertyValue1);
        void Information<T0, T1, T2>(string msgTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2);

        void Warning(string msg);
        void Warning<T>(string msgTemplate, T propertyValue);
        void Warning<T0, T1>(string msgTemplate, T0 propertyValue0, T1 propertyValue1);
        void Warning<T0, T1, T2>(string msgTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2);

        void Error(string msg);
        void Error<T>(string msgTemplate, T propertyValue);
        void Error<T0, T1>(string msgTemplate, T0 propertyValue0, T1 propertyValue1);
        void Error<T0, T1, T2>(string msgTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2);

        void Fatal(string msg);
        void Fatal<T>(string msgTemplate, T propertyValue);
        void Fatal<T0, T1>(string msgTemplate, T0 propertyValue0, T1 propertyValue1);
        void Fatal<T0, T1, T2>(string msgTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2);

    }
}
