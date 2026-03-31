using Communication.ModBus.Common;

namespace Communication.ModBus.Utils
{
   /// <summary>
   /// Discarded class.
   /// </summary>
   public sealed class Serilogger : IDisposable
   {
       private bool disposed = false;
       private static readonly object loggerLock = new();
       private static int instanceSet = 0;

       private static volatile ISerilog? instance;
       public static ISerilog? Instance
       {
           get
           {
               return instance;
           }
       }

       private Serilogger() { }

       public static void SetInstance(ISerilog logger)
       {
           ArgumentNullException.ThrowIfNull(logger);

           if (Interlocked.CompareExchange(ref instanceSet, 1, 0) != 0)
           {
               throw new InvalidOperationException($"{nameof(Instance)} has been initialized!");
           }

           lock (loggerLock)
           {
               if (Instance != null)
               {
                   Interlocked.Exchange(ref instanceSet, 0);
                   throw new InvalidOperationException($"{nameof(Instance)} has been initialized!");
               }

               instance = logger;
           }
       }

       public void Dispose()
       {
           if (disposed)
               return;

           GC.SuppressFinalize(this);
           disposed = true;
           instance = null;
           Interlocked.Exchange(ref instanceSet, 0);
       }

   }
}
