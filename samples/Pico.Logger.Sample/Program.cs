// Initialize DI container and configure logging

var container = Bootstrap.CreateContainer();

// Register console logger with application type
container.RegisterLogger();

// Create typed logger instance
var logger = container.GetProvider().Resolve<ILogger<Program>>();

// Demonstrate basic async logging
await logger.InfoAsync("Hello, World!");

// Log all severity levels with contextual examples
logger.Trace("1. Verbose diagnostic tracing"); // Finest-grained tracing
logger.Debug("2. Database query executed in 12ms"); // Debug-level details
logger.Info("3. Application initialized successfully"); // Normal operation
logger.Notice("4. User 'admin' logged in from 192.168.1.1"); // Significant event
logger.Warning("5. High CPU usage detected (90%)"); // Potential issues
logger.Error(
    "6. Failed to save user profile", // Recoverable errors
    new InvalidOperationException("File locked")
);
logger.Critical("7. Payment gateway unreachable"); // Critical failures
logger.Alert("8. Security firewall breached"); // Immediate action needed
logger.Emergency("9. System storage full - service halted"); // System-wide outage

// Demonstrate async logging patterns
await logger.TraceAsync("10. Async trace: Cache refresh started");
await logger.DebugAsync("11. Async debug: Deserializing payload");
await logger.NoticeAsync("12. Async notice: New user registration");
await logger.AlertAsync("13. Async alert: Brute force attack detected");

// Structured logging with scopes
using (logger.BeginScope("OrderProcessing initiated"))
{
    logger.Debug($"14. Order ID: {12345}");
    logger.Notice($"15. User ID: {67890}");
    logger.Warning($"16. Processing time: {150}ms");

    {
        logger.Info("17. Starting order processing workflow");

        try
        {
            // Nested scope for payment operations
            using (logger.BeginScope("OrderPayment"))
            {
                logger.Debug("18. Validating order items");
                logger.Notice("19. Processing payment for order");

                // Simulate business logic failure
                throw new DivideByZeroException();
            }
        }
        catch (Exception ex)
        {
            // Error logging with exception context
            logger.Error("20. Payment processing failed", ex);
            logger.Critical("21. Order workflow cannot continue");
        }
    }
}

// Performance-sensitive logging demonstration
var stopwatch = Stopwatch.StartNew();
logger.Debug("22. Starting data export...");
await Task.Delay(250); // Simulate work
logger.Debug($"23. Export completed in {stopwatch.ElapsedMilliseconds}ms");

// Configuration demonstration
// logger.Info($"Current log level: {logger.MinimumLevel}");
// logger.Info($"Log output targets: {logger.Targets}");

// Graceful shutdown example
logger.Notice("24. Application shutting down...");
logger.Info("25. Press any key to exit...");
Console.ReadKey();
