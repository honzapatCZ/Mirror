using System;
using FlaxEngine;

namespace Mirror.Logging
{
    public class ConsoleColorLogHandler : ILogHandler
    {
        readonly bool showExceptionStackTrace;

        public ConsoleColorLogHandler(bool showExceptionStackTrace)
        {
            this.showExceptionStackTrace = showExceptionStackTrace;
        }

        public event LogDelegate SendLog;
        public event LogExceptionDelegate SendExceptionLog;

        public void Log(LogType logType, FlaxEngine.Object context, string message)
        {
            switch (logType)
            {
                case LogType.Error:
                case LogType.Fatal:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case LogType.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
            }

            Console.WriteLine(message);
            Console.ResetColor();
        }

        public void LogException(Exception exception, FlaxEngine.Object context)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Exception: {exception.Message}");
            if (showExceptionStackTrace)
            {
                Console.WriteLine($"    {exception.StackTrace}");
            }
            Console.ResetColor();
        }

        public void LogWrite(LogType logType, string message)
        {
            switch (logType)
            {
                case LogType.Error:
                case LogType.Fatal:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case LogType.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
            }

            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}
