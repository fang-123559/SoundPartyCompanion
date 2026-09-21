namespace SoundPartyCompanion.Core.Diagnostics;

public interface IAppLogger
{
    void Write(AppLogLevel level, string message, Exception? exception = null);
}

