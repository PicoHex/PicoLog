namespace Pico.Logger.Abs;

public enum LogLevel : byte
{
    Emergency = 0,
    Alert = 1,
    Critical = 2,
    Error = 3,
    Warning = 4,
    Notice = 5,
    Info = 6,
    Debug = 7,
    Trace = 8,
    None = byte.MaxValue
}
