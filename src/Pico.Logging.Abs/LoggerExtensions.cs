namespace Pico.Logging.Abs;

public static class LoggerExtensions
{
    extension(ILogger logger)
    {
        /// <summary>
        /// Logs a message at <see cref="LogLevel.Trace"/> level.
        /// </summary>
        /// <remarks>
        /// Use for detailed debugging information that's typically only relevant during development.
        /// </remarks>
        public void Trace(string message) =>
            logger.Log(LogLevel.Trace, message);

        /// <summary>
        /// Logs a message at <see cref="LogLevel.Debug"/> level.
        /// </summary>
        /// <remarks>
        /// Use for debugging information that's useful in production troubleshooting.
        /// </remarks>
        public void Debug(string message) =>
            logger.Log(LogLevel.Debug, message);

        /// <summary>
        /// Logs a message at <see cref="LogLevel.Info"/> level.
        /// </summary>
        /// <remarks>
        /// Use for general application flow tracking and significant events.
        /// </remarks>
        public void Info(string message) =>
            logger.Log(LogLevel.Info, message);

        /// <summary>
        /// Logs a message at <see cref="LogLevel.Notice"/> level.
        /// </summary>
        /// <remarks>
        /// Use for important runtime events that require attention but aren't errors.
        /// </remarks>
        public void Notice(string message, Exception? exception = null) =>
            logger.Log(LogLevel.Notice, message, exception);

        /// <summary>
        /// Logs a message at <see cref="LogLevel.Warning"/> level.
        /// </summary>
        /// <remarks>
        /// Use for unexpected or problematic situations that aren't immediately harmful.
        /// </remarks>
        public void Warning(string message, Exception? exception = null) =>
            logger.Log(LogLevel.Warning, message, exception);

        /// <summary>
        /// Logs a message at <see cref="LogLevel.Error"/> level.
        /// </summary>
        /// <remarks>
        /// Use for errors that impact specific operations but allow the application to continue.
        /// </remarks>
        public void Error(string message, Exception? exception = null) =>
            logger.Log(LogLevel.Error, message, exception);

        /// <summary>
        /// Logs a message at <see cref="LogLevel.Critical"/> level.
        /// </summary>
        /// <remarks>
        /// Use for severe failures that require immediate attention and may crash the application.
        /// </remarks>
        public void Critical(string message, Exception? exception = null) =>
            logger.Log(LogLevel.Critical, message, exception);

        /// <summary>
        /// Logs a message at <see cref="LogLevel.Alert"/> level.
        /// </summary>
        /// <remarks>
        /// Use when immediate action is required (e.g., loss of primary database connection).
        /// </remarks>
        public void Alert(string message, Exception? exception = null) =>
            logger.Log(LogLevel.Alert, message, exception);

        /// <summary>
        /// Logs a message at <see cref="LogLevel.Emergency"/> level.
        /// </summary>
        /// <remarks>
        /// Reserve for catastrophic system-wide failures where the system is unusable.
        /// </remarks>
        public void Emergency(string message,
            Exception? exception = null
        ) => logger.Log(LogLevel.Emergency, message, exception);

        /// <summary>
        /// Asynchronously logs a message at <see cref="LogLevel.Trace"/> level.
        /// </summary>
        /// <remarks>
        /// Prefer this async version for I/O-bound operations in async contexts.
        /// </remarks>
        public Task TraceAsync(string message,
            CancellationToken cancellationToken = default
        ) => logger.LogAsync(LogLevel.Trace, message, cancellationToken: cancellationToken);

        /// <summary>
        /// Asynchronously logs a message at <see cref="LogLevel.Debug"/> level.
        /// </summary>
        /// <remarks>
        /// Use for debugging information that's useful in production troubleshooting.
        /// </remarks>
        public Task DebugAsync(string message,
            CancellationToken cancellationToken = default
        ) => logger.LogAsync(LogLevel.Debug, message, cancellationToken: cancellationToken);

        /// <summary>
        /// Asynchronously logs a message at <see cref="LogLevel.Info"/> level.
        /// </summary>
        /// <remarks>
        /// Use for general application flow tracking and significant events.
        /// </remarks>
        public Task InfoAsync(string message,
            CancellationToken cancellationToken = default
        ) => logger.LogAsync(LogLevel.Info, message, cancellationToken: cancellationToken);

        /// <summary>
        /// Asynchronously logs a message at <see cref="LogLevel.Notice"/> level.
        /// </summary>
        /// <remarks>
        /// Use for important runtime events that require attention but aren't errors.
        /// </remarks>
        public Task NoticeAsync(string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        ) => logger.LogAsync(LogLevel.Notice, message, exception, cancellationToken);

        /// <summary>
        /// Asynchronously logs a message at <see cref="LogLevel.Warning"/> level.
        /// </summary>
        /// <remarks>
        /// Use for unexpected or problematic situations that aren't immediately harmful.
        /// </remarks>
        public Task WarningAsync(string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        ) => logger.LogAsync(LogLevel.Warning, message, exception, cancellationToken);

        /// <summary>
        /// Asynchronously logs a message at <see cref="LogLevel.Error"/> level.
        /// </summary>
        /// <remarks>
        /// Use for errors that impact specific operations but allow the application to continue.
        /// </remarks>
        public Task ErrorAsync(string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        ) => logger.LogAsync(LogLevel.Error, message, exception, cancellationToken);

        /// <summary>
        /// Asynchronously logs a message at <see cref="LogLevel.Critical"/> level.
        /// </summary>
        /// <remarks>
        /// Use for severe failures that require immediate attention and may crash the application.
        /// </remarks>
        public Task CriticalAsync(string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        ) => logger.LogAsync(LogLevel.Critical, message, exception, cancellationToken);

        /// <summary>
        /// Asynchronously logs a message at <see cref="LogLevel.Alert"/> level.
        /// </summary>
        /// <remarks>
        /// Use when immediate action is required (e.g., loss of primary database connection).
        /// </remarks>
        public Task AlertAsync(string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        ) => logger.LogAsync(LogLevel.Alert, message, exception, cancellationToken);

        /// <summary>
        /// Asynchronously logs a message at <see cref="LogLevel.Emergency"/> level.
        /// </summary>
        /// <remarks>
        /// Reserve for catastrophic system-wide failures where the system is unusable.
        /// </remarks>
        public Task EmergencyAsync(string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default
        ) => logger.LogAsync(LogLevel.Emergency, message, exception, cancellationToken);
    }
}
