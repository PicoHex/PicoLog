namespace PicoLog.Sample;

public class Service(ILogger<Service> logger) : IService
{
    public async Task WriteLogAsync()
    {
        await logger.InfoAsync("Hello, World!");

        // Emit messages across the supported severity levels.
        logger.Trace("1. Verbose diagnostic tracing");
        logger.Debug("2. Database query executed in 12ms");
        logger.Info("3. Application initialized successfully");
        logger.Notice("4. User 'admin' logged in from 192.168.1.1");
        logger.Warning("5. High CPU usage detected (90%)");
        logger.Error(
            "6. Failed to save user profile",
            new InvalidOperationException("File locked")
        );
        logger.Critical("7. Payment gateway unreachable");
        logger.Alert("8. Security firewall breached");
        logger.Emergency("9. System storage full - service halted");

        // Repeat the same pattern through the async API.
        await logger.TraceAsync("10. Async trace: Cache refresh started");
        await logger.DebugAsync("11. Async debug: Deserializing payload");
        await logger.NoticeAsync("12. Async notice: New user registration");
        await logger.AlertAsync("13. Async alert: Brute force attack detected");

        // Capture nested scopes and exception logging.
        using (logger.BeginScope("OrderProcessing initiated"))
        {
            logger.Debug($"14. Order ID: {12345}");
            logger.Notice($"15. User ID: {67890}");
            logger.Warning($"16. Processing time: {150}ms");

            {
                logger.Info("17. Starting order processing workflow");

                try
                {
                    using (logger.BeginScope("OrderPayment"))
                    {
                        logger.Debug("18. Validating order items");
                        logger.Notice("19. Processing payment for order");

                        throw new DivideByZeroException();
                    }
                }
                catch (Exception ex)
                {
                    logger.Error("20. Payment processing failed", ex);
                    logger.Critical("21. Order workflow cannot continue");
                }
            }
        }

        // Record a simple timing example.
        var stopwatch = Stopwatch.StartNew();
        logger.Debug("22. Starting data export...");
        await Task.Delay(250);
        await logger.DebugAsync($"23. Export completed in {stopwatch.ElapsedMilliseconds}ms");

        // Finish with two shutdown messages that should survive factory disposal.
        await logger.NoticeAsync("24. Application shutting down...");
        await logger.InfoAsync("25. Press any key to exit...");

        logger.Log(
            LogLevel.Info,
            "26. Export pipeline finished",
            [new("records", 128), new("elapsedMs", stopwatch.ElapsedMilliseconds)],
            exception: null
        );
    }
}
