namespace Pico.Logger.Abs;

public static class LoggerExtensions
{
    /// <summary>
    /// Logs a message at <see cref="LogLevel.Trace"/> level.
    /// </summary>
    /// <remarks>
    /// Use for detailed debugging information that's typically only relevant during development.
    /// </remarks>
    public static void Trace(this ILogger logger, string message) =>
        logger.Log(LogLevel.Trace, message);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Debug"/> level.
    /// </summary>
    /// <remarks>
    /// Use for debugging information that's useful in production troubleshooting.
    /// </remarks>
    public static void Debug(this ILogger logger, string message) =>
        logger.Log(LogLevel.Debug, message);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Info"/> level.
    /// </summary>
    /// <remarks>
    /// Use for general application flow tracking and significant events.
    /// </remarks>
    public static void Info(this ILogger logger, string message) =>
        logger.Log(LogLevel.Info, message);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Notice"/> level.
    /// </summary>
    /// <remarks>
    /// Use for important runtime events that require attention but aren't errors.
    /// </remarks>
    public static void Notice(this ILogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Notice, message, exception);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Warning"/> level.
    /// </summary>
    /// <remarks>
    /// Use for unexpected or problematic situations that aren't immediately harmful.
    /// </remarks>
    public static void Warning(this ILogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Warning, message, exception);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Error"/> level.
    /// </summary>
    /// <remarks>
    /// Use for errors that impact specific operations but allow the application to continue.
    /// </remarks>
    public static void Error(this ILogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Error, message, exception);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Critical"/> level.
    /// </summary>
    /// <remarks>
    /// Use for severe failures that require immediate attention and may crash the application.
    /// </remarks>
    public static void Critical(this ILogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Critical, message, exception);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Alert"/> level.
    /// </summary>
    /// <remarks>
    /// Use when immediate action is required (e.g., loss of primary database connection).
    /// </remarks>
    public static void Alert(this ILogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Alert, message, exception);

    /// <summary>
    /// Logs a message at <see cref="LogLevel.Emergency"/> level.
    /// </summary>
    /// <remarks>
    /// Reserve for catastrophic system-wide failures where the system is unusable.
    /// </remarks>
    public static void Emergency(
        this ILogger logger,
        string message,
        Exception? exception = null
    ) => logger.Log(LogLevel.Emergency, message, exception);

    /// <summary>
    /// Asynchronously logs a message at <see cref="LogLevel.Trace"/> level.
    /// </summary>
    /// <remarks>
    /// Prefer this async version for I/O-bound operations in async contexts.
    /// </remarks>
    public static ValueTask TraceAsync(
        this ILogger logger,
        string message,
        CancellationToken cancellationToken = default
    ) => logger.LogAsync(LogLevel.Trace, message, cancellationToken: cancellationToken);

    /// <summary>
    /// Asynchronously logs a message at <see cref="LogLevel.Debug"/> level.
    /// </summary>
    /// <remarks>
    /// Use for debugging information that's useful in production troubleshooting.
    /// </remarks>
    public static ValueTask DebugAsync(
        this ILogger logger,
        string message,
        CancellationToken cancellationToken = default
    ) => logger.LogAsync(LogLevel.Debug, message, cancellationToken: cancellationToken);

    /// <summary>
    /// Asynchronously logs a message at <see cref="LogLevel.Info"/> level.
    /// </summary>
    /// <remarks>
    /// Use for general application flow tracking and significant events.
    /// </remarks>
    public static ValueTask InfoAsync(
        this ILogger logger,
        string message,
        CancellationToken cancellationToken = default
    ) => logger.LogAsync(LogLevel.Info, message, cancellationToken: cancellationToken);

    /// <summary>
    /// Asynchronously logs a message at <see cref="LogLevel.Notice"/> level.
    /// </summary>
    /// <remarks>
    /// Use for important runtime events that require attention but aren't errors.
    /// </remarks>
    public static ValueTask NoticeAsync(
        this ILogger logger,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => logger.LogAsync(LogLevel.Notice, message, exception, cancellationToken);

    /// <summary>
    /// Asynchronously logs a message at <see cref="LogLevel.Warning"/> level.
    /// </summary>
    /// <remarks>
    /// Use for unexpected or problematic situations that aren't immediately harmful.
    /// </remarks>
    public static ValueTask WarningAsync(
        this ILogger logger,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => logger.LogAsync(LogLevel.Warning, message, exception, cancellationToken);

    /// <summary>
    /// Asynchronously logs a message at <see cref="LogLevel.Error"/> level.
    /// </summary>
    /// <remarks>
    /// Use for errors that impact specific operations but allow the application to continue.
    /// </remarks>
    public static ValueTask ErrorAsync(
        this ILogger logger,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => logger.LogAsync(LogLevel.Error, message, exception, cancellationToken);

    /// <summary>
    /// Asynchronously logs a message at <see cref="LogLevel.Critical"/> level.
    /// </summary>
    /// <remarks>
    /// Use for severe failures that require immediate attention and may crash the application.
    /// </remarks>
    public static ValueTask CriticalAsync(
        this ILogger logger,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => logger.LogAsync(LogLevel.Critical, message, exception, cancellationToken);

    /// <summary>
    /// Asynchronously logs a message at <see cref="LogLevel.Alert"/> level.
    /// </summary>
    /// <remarks>
    /// Use when immediate action is required (e.g., loss of primary database connection).
    /// </remarks>
    public static ValueTask AlertAsync(
        this ILogger logger,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => logger.LogAsync(LogLevel.Alert, message, exception, cancellationToken);

    /// <summary>
    /// Asynchronously logs a message at <see cref="LogLevel.Emergency"/> level.
    /// </summary>
    /// <remarks>
    /// Reserve for catastrophic system-wide failures where the system is unusable.
    /// </remarks>
    public static ValueTask EmergencyAsync(
        this ILogger logger,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default
    ) => logger.LogAsync(LogLevel.Emergency, message, exception, cancellationToken);
}
