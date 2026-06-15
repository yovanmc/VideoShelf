using VideoShelf.Core.Storage;

namespace VideoShelf.App.Tests.TestSupport;

/// <summary>Factory helpers to create pre-configured SettingsRepository instances for unit tests.
/// Each factory method owns its own temp DB and the caller is responsible for disposal.</summary>
public static class FakeSettings
{
    /// <summary>Returns a SettingsRepository backed by a fresh temp DB with probe_concurrency pre-set.</summary>
    public static (SettingsRepository Settings, AppTempDb TempDb) WithProbeConcurrency(int degree)
    {
        var temp = new AppTempDb();
        var settings = new SettingsRepository(temp.Db);
        settings.SetString(SettingsRepository.ProbeConcurrencyKey, degree.ToString());
        return (settings, temp);
    }
}
