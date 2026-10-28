using System.Runtime.CompilerServices;
using EDC.Core;

namespace EDC.Tests;

/// <summary>The tests read the texts in French, whatever the language of the machine running them.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void French() => Loc.Apply("fr");
}
