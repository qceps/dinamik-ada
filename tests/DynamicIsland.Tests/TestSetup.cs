using System.Runtime.CompilerServices;
using DynamicIsland.Core.Diagnostics;

namespace DynamicIsland.Tests;

internal static class TestSetup
{
    /// <summary>Testler (bilerek hata üreten olanlar dahil) kullanıcının gerçek uygulama günlüğüne yazmasın.</summary>
    [ModuleInitializer]
    internal static void RedirectLog() =>
        Log.Configure(Path.Combine(Path.GetTempPath(), "DinamikAda-test-logs"));
}
