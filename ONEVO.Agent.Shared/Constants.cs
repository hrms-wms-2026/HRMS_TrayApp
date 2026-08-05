namespace ONEVO.Agent.Shared;

public static class Constants
{
    public const string PipeName = "ONEVO.Agent.Pipe.v1";
    public const int MaxMessageLengthBytes = 65_536;
    public const int IpcConnectionTimeoutMs = 5_000;
    public const int NonceLengthBytes = 32;
    public const int ReconnectMaxAttempts = 5;
    public const int ReconnectBaseDelayMs = 1_000;
}
